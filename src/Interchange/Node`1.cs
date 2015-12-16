using Interchange.Headers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Interchange
{
    public class Node<TTag> : IDisposable
    {
        private readonly EndPoint LocalEndPoint = new IPEndPoint(IPAddress.Any, 0);

        Socket socket;

        ObjectPool<Packet> packetPool;
        ObjectPool<SocketAsyncEventArgs> socketEventArgsPool;

        public IReadOnlyObjectPool<Packet> PacketPool {
            get { return packetPool; }
        }

        public IReadOnlyObjectPool<SocketAsyncEventArgs> SocketEventArgsPool {
            get { return socketEventArgsPool; }
        }

        public Action<Packet> ProcessIncomingMessageAction { get; set; }
        public Action<EndPoint> ProcessConnected { get; set; }

        ConcurrentDictionary<EndPoint, Connection<TTag>> connections;

        TaskCompletionSource<bool> connectTcs;

        CancellationToken updateCancellationToken;

        bool client = false;
        bool disposed;

        public Connection<TTag> RemoteConnection {
            get {
                return connections.First().Value;
            }
        }

        public Node() : this(new NodeConfiguration()) {
        }

        public Node(NodeConfiguration configuration) {
            // TODO: Not actually random yet
            Random random = new Random();

            packetPool = new ObjectPool<Packet>();
            packetPool.SeedPool(() => { return new Packet(packetPool, new byte[configuration.MTU]); }, configuration.BufferPoolSize);

            socketEventArgsPool = new ObjectPool<SocketAsyncEventArgs>(() =>
            {
                var eventArgs = new SocketAsyncEventArgs();
                eventArgs.Completed += IO_Completed;

                return eventArgs;
            }, configuration.SocketEventPoolSize);

            connections = new ConcurrentDictionary<EndPoint, Connection<TTag>>();

            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            // Begin updating
            updateCancellationToken = new CancellationToken();
            Task.Run(Update, updateCancellationToken);
        }

        public Task<Packet> RequestNewPacket() {
            var packet = packetPool.GetObject();
            packet.Initialize();
            return Task.FromResult(packet);
        }

        private async Task Update() {
            while (true) {
                foreach (var connection in connections) {
                    await connection.Value.Update();
                }

                await Task.Delay(1);
            }
        }

        private void Close() {
            socket?.Dispose();
        }

        public async Task ListenAsync(IPAddress localIPAddress, int port) {
            IPEndPoint ipEndPoint = new IPEndPoint(localIPAddress, port);
            socket.Bind(ipEndPoint);

            await PerformReceive();
        }

        public async Task SendDataAsync(Connection<TTag> connection, byte[] buffer) {
            await SendReliableDataPacket(connection, buffer);
        }

        public async Task ConnectAsync(EndPoint endPoint) {
            var connection = new Connection<TTag>(this, endPoint);
            if (!connections.TryAdd(endPoint, connection)) {
                // TODO: Couldn't add the connection
                throw new NotImplementedException();
            }

            client = true;

            connectTcs = new TaskCompletionSource<bool>();

            await ListenAsync(IPAddress.Any, 0);
            await SendInternalPacket(connection, MessageType.Syn);

            await connectTcs.Task;
        }

        internal async Task PerformSend(EndPoint endPoint, Packet packet) {
            try {
                var eventArgs = socketEventArgsPool.GetObject();
                eventArgs.RemoteEndPoint = endPoint;
                eventArgs.SetBuffer(packet.BackingBuffer, packet.Payload.Offset, packet.Payload.Count);
                eventArgs.UserToken = packet;

                bool willRaiseEvent = socket.SendToAsync(eventArgs);
                if (!willRaiseEvent) {
                    await HandlePacketSent(eventArgs);

                    socketEventArgsPool.ReleaseObject(eventArgs);
                }
            } catch (ObjectDisposedException) {
                // TODO: Properly dispose of this object
            } catch (System.IO.IOException) {
                // TODO: Properly handle these exceptions
            }
        }

        private async Task PerformReceive() {
            try {
                var eventArgs = socketEventArgsPool.GetObject();
                var packet = await RequestNewPacket();
                eventArgs.SetBuffer(packet.BackingBuffer, 0, packet.BackingBuffer.Length);
                eventArgs.RemoteEndPoint = LocalEndPoint;
                eventArgs.UserToken = packet;

                bool willRaiseEvent = socket.ReceiveFromAsync(eventArgs);
                if (!willRaiseEvent) {
                    await HandlePacketReceived(eventArgs);

                    socketEventArgsPool.ReleaseObject(eventArgs);
                }
            } catch (ObjectDisposedException) {
                // TODO: Properly dispose of this object
            } catch (System.IO.IOException) {
                // TODO: Properly handle these exceptions
            }
        }

        private async void IO_Completed(object sender, SocketAsyncEventArgs e) {
            switch (e.LastOperation) {
                case SocketAsyncOperation.ReceiveFrom:
                    await HandlePacketReceived(e);
                    break;
                case SocketAsyncOperation.SendTo:
                    await HandlePacketSent(e);
                    break;
                default:
                    throw new NotImplementedException();
            }

            socketEventArgsPool.ReleaseObject(e);
        }

        private async Task HandlePacketReceived(SocketAsyncEventArgs e) {
            ArraySegment<byte> segment = new ArraySegment<byte>(e.Buffer, e.Offset, e.BytesTransferred);

            bool handled = false;

            if (segment.Count > 0) {
                MessageType messageType = (MessageType)segment.Array[segment.Offset];

                Connection<TTag> connection;
                if (connections.TryGetValue(e.RemoteEndPoint, out connection)) {
                    switch (messageType) {
                        case MessageType.Syn:
                            // TODO: Reject the connection, already connected!
                            throw new NotImplementedException();
                        case MessageType.SynAck: {
                                var header = SynAckHeader.FromSegment(segment);

                                connection.InitializeAckNumber(header.SequenceNumber);

                                // Syn-ack received, confirm and establish the connection
                                await SendAckPacket(connection);

                                connection.State = ConnectionState.Connected;
                                if (ProcessConnected != null) {
                                    ProcessConnected(e.RemoteEndPoint);
                                }

                                connectTcs.TrySetResult(true);
                            }
                            break;
                        case MessageType.Ack: {
                                var header = AckHeader.FromSegment(segment);

                                if (header.SequenceNumber == (ushort)(connection.AckNumber - 1)) {
                                    if (ProcessConnected != null) {
                                        ProcessConnected(e.RemoteEndPoint);
                                    }
                                } else {
                                    // RecordAck will dispose the stored outgoing packet
                                    // Leave this unhandled to allow for the incoming ack packet itself to be disposed
                                    connection.PacketTransmissionController.RecordAck(connection, header.SequenceNumber);
                                }
                                break;
                            }
                        case MessageType.ReliableData: {
                                var header = ReliableDataHeader.FromSegment(segment);

                                Packet packet = (Packet)e.UserToken;
                                packet.MarkPayloadRegion(segment.Offset + 1 + 16 + 16, header.PayloadSize);

                                // TODO: Cache out-of-order packets, release them in order as new packets arrive
                                if (header.SequenceNumber == connection.AckNumber) {
                                    handled = true;

                                    await SendAckPacket(connection);

                                    if (ProcessIncomingMessageAction != null) {
                                        ProcessIncomingMessageAction(packet);
                                    } else {
                                        // If there is nothing using this packet, it can be disposed right away
                                        packet.Dispose();
                                    }
                                }
                                break;
                            }
                    }
                } else {
                    switch (messageType) {
                        case MessageType.Syn: {
                                var header = SynHeader.FromSegment(segment);

                                // Received a connection attempt
                                connection = new Connection<TTag>(this, e.RemoteEndPoint);
                                if (connections.TryAdd(e.RemoteEndPoint, connection)) {
                                    // TODO: All good, raise events
                                    connection.State = ConnectionState.HandshakeInitiated;
                                    connection.InitializeAckNumber(header.SequenceNumber);
                                    await SendSynAckPacket(connection);
                                } else {
                                    // Couldn't add to the connections dictionary - uh oh!
                                    throw new NotImplementedException();
                                }
                                break;
                            }
                        case MessageType.SynAck: {
                                // TODO: Got a synack, but no local connection has been initiated
                                throw new NotImplementedException();
                            }
                    }
                }
            }

            if (!handled) {
                Packet packet = (Packet)e.UserToken;
                packet.Dispose();
            }

            // Continue listening for new packets
            if (!disposed) {
                await PerformReceive();
            }
        }

        private async Task HandlePacketSent(SocketAsyncEventArgs e) {
            Packet packet = (Packet)e.UserToken;
            if (packet.CandidateForDispoal) {
                packet.Dispose();
            }
        }

        private async Task SendToSequenced(Connection<TTag> connection, ushort sequenceNumber, Packet packet) {
            connection.PacketTransmissionController.RecordPacketTransmission(sequenceNumber, connection, packet);

            await PerformSend(connection.RemoteEndPoint, packet);
        }

        private async Task SendInternalPacket(Connection<TTag> connection, MessageType messageType) {
            var packet = await RequestNewPacket();
            packet.MarkPayloadRegion(0, 1 + 16);

            packet.BackingBuffer[0] = (byte)messageType;

            // TODO: Remove the unneeded byte[] allocation
            byte[] sqnBytes = BitConverter.GetBytes(connection.SequenceNumber);
            Buffer.BlockCopy(sqnBytes, 0, packet.BackingBuffer, 1, sqnBytes.Length);

            connection.IncrementSequenceNumber();

            await PerformSend(connection.RemoteEndPoint, packet);
        }

        private async Task SendSynAckPacket(Connection<TTag> connection) {
            var packet = await RequestNewPacket();
            packet.MarkPayloadRegion(0, 1 + 16 + 16);

            packet.BackingBuffer[0] = (byte)MessageType.SynAck;

            // TODO: Remove the unneeded byte[] allocation
            byte[] sqnBytes = BitConverter.GetBytes(connection.SequenceNumber);
            Buffer.BlockCopy(sqnBytes, 0, packet.BackingBuffer, 1, sqnBytes.Length);

            // TODO: Remove the unneeded byte[] allocation
            byte[] ackBytes = BitConverter.GetBytes(connection.AckNumber);
            Buffer.BlockCopy(ackBytes, 0, packet.BackingBuffer, 17, ackBytes.Length);

            connection.IncrementSequenceNumber();
            connection.IncrementAckNumber();

            //await SendToSequenced(endPoint, sequenceNumber, buffer);
            await PerformSend(connection.RemoteEndPoint, packet);
        }

        private async Task SendAckPacket(Connection<TTag> connection) {
            var packet = await RequestNewPacket();
            packet.MarkPayloadRegion(0, 1 + 16);

            packet.BackingBuffer[0] = (byte)MessageType.Ack;

            // TODO: Remove the unneeded byte[] allocation
            byte[] ackBytes = BitConverter.GetBytes(connection.AckNumber);
            Buffer.BlockCopy(ackBytes, 0, packet.BackingBuffer, 1, ackBytes.Length);

            connection.IncrementAckNumber();

            await PerformSend(connection.RemoteEndPoint, packet);
        }

        private async Task SendReliableDataPacket(Connection<TTag> connection, byte[] buffer) {
            var packet = await RequestNewPacket();
            packet.MarkPayloadRegion(0, 1 + 16 + 16 + buffer.Length);

            packet.BackingBuffer[0] = (byte)MessageType.ReliableData;

            ushort packetSequenceNumber = connection.SequenceNumber;

            // TODO: Remove the unneeded byte[] allocation
            byte[] sqnBytes = BitConverter.GetBytes(connection.SequenceNumber);
            Buffer.BlockCopy(sqnBytes, 0, packet.BackingBuffer, 1, sqnBytes.Length);

            byte[] sizeBytes = BitConverter.GetBytes((ushort)buffer.Length);
            Buffer.BlockCopy(sizeBytes, 0, packet.BackingBuffer, 1 + 16, sizeBytes.Length);

            Buffer.BlockCopy(buffer, 0, packet.BackingBuffer, 1 + 16 + 16, buffer.Length);

            connection.IncrementSequenceNumber();

            await SendToSequenced(connection, packetSequenceNumber, packet);
        }

        public void Dispose() {
            if (!disposed) {
                disposed = true;
                Close();
            }
        }
    }
}
