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

        ObjectPool<byte[]> bufferPool;
        ObjectPool<SocketAsyncEventArgs> socketEventArgsPool;

        public Action<ArraySegment<byte>> ProcessIncomingMessageAction { get; set; }
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

            bufferPool = new ObjectPool<byte[]>(() => { return new byte[configuration.MTU]; }, configuration.BufferPoolSize);

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

            await ListenAsync(IPAddress.Any, 0);
            await SendInternalPacket(connection, MessageType.Syn);

            client = true;

            connectTcs = new TaskCompletionSource<bool>();

            await connectTcs.Task;
        }

        internal async Task PerformSend(EndPoint endPoint, byte[] buffer) {
            try {
                var eventArgs = socketEventArgsPool.GetObject();
                eventArgs.RemoteEndPoint = endPoint;
                eventArgs.SetBuffer(buffer, 0, buffer.Length);

                bool willRaiseEvent = socket.SendToAsync(eventArgs);
                if (!willRaiseEvent) {
                    await HandlePacketSent(eventArgs);

                    socketEventArgsPool.ReleaseObject(eventArgs);
                }
            } catch (System.IO.IOException) {
                // TODO: Properly handle these exceptions
            }
        }

        private async Task PerformReceive() {
            try {
                var eventArgs = socketEventArgsPool.GetObject();
                var readBuffer = bufferPool.GetObject();
                eventArgs.SetBuffer(readBuffer, 0, readBuffer.Length);
                eventArgs.RemoteEndPoint = LocalEndPoint;

                bool willRaiseEvent = socket.ReceiveFromAsync(eventArgs);
                if (!willRaiseEvent) {
                    await HandlePacketReceived(eventArgs);

                    socketEventArgsPool.ReleaseObject(eventArgs);
                }
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
                                    connection.PacketTransmissionController.RecordAck(connection, header.SequenceNumber);
                                }
                                break;
                            }
                        case MessageType.ReliableData: {
                                var header = ReliableDataHeader.FromSegment(segment);

                                ArraySegment<byte> dataBuffer = new ArraySegment<byte>(segment.Array, segment.Offset + 1 + 16 + 16, header.PayloadSize);

                                // TODO: Cache out-of-order packets, release them in order as new packets arrive
                                if (header.SequenceNumber == connection.AckNumber) {
                                    await SendAckPacket(connection);

                                    if (ProcessIncomingMessageAction != null) {
                                        ProcessIncomingMessageAction(dataBuffer);
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

            // TODO: For now, release read buffer here. Later, release the read buffer from a dedicated packet object
            bufferPool.ReleaseObject(e.Buffer);

            // Continue listening for new packets
            if (!disposed) {
                await PerformReceive();
            }
        }

        private async Task HandlePacketSent(SocketAsyncEventArgs e) {

        }

        private async Task SendToSequenced(Connection<TTag> connection, ushort sequenceNumber, byte[] buffer) {
            connection.PacketTransmissionController.RecordPacketTransmission(sequenceNumber, connection, buffer);

            await PerformSend(connection.RemoteEndPoint, buffer);
        }

        private async Task SendInternalPacket(Connection<TTag> connection, MessageType messageType) {
            byte[] buffer = new byte[1 + 16];
            buffer[0] = (byte)messageType;

            // TODO: Remove the unneeded byte[] allocation
            byte[] sqnBytes = BitConverter.GetBytes(connection.SequenceNumber);
            Buffer.BlockCopy(sqnBytes, 0, buffer, 1, sqnBytes.Length);

            connection.IncrementSequenceNumber();

            await PerformSend(connection.RemoteEndPoint, buffer);
        }

        private async Task SendSynAckPacket(Connection<TTag> connection) {
            byte[] buffer = new byte[1 + 16 + 16];
            buffer[0] = (byte)MessageType.SynAck;

            // TODO: Remove the unneeded byte[] allocation
            byte[] sqnBytes = BitConverter.GetBytes(connection.SequenceNumber);
            Buffer.BlockCopy(sqnBytes, 0, buffer, 1, sqnBytes.Length);

            // TODO: Remove the unneeded byte[] allocation
            byte[] ackBytes = BitConverter.GetBytes(connection.AckNumber);
            Buffer.BlockCopy(ackBytes, 0, buffer, 17, ackBytes.Length);

            connection.IncrementSequenceNumber();
            connection.IncrementAckNumber();

            //await SendToSequenced(endPoint, sequenceNumber, buffer);
            await PerformSend(connection.RemoteEndPoint, buffer);
        }

        private async Task SendAckPacket(Connection<TTag> connection) {
            byte[] buffer = new byte[1 + 16];
            buffer[0] = (byte)MessageType.Ack;

            // TODO: Remove the unneeded byte[] allocation
            byte[] ackBytes = BitConverter.GetBytes(connection.AckNumber);
            Buffer.BlockCopy(ackBytes, 0, buffer, 1, ackBytes.Length);

            connection.IncrementAckNumber();

            await PerformSend(connection.RemoteEndPoint, buffer);
        }

        private async Task SendReliableDataPacket(Connection<TTag> connection, byte[] buffer) {
            byte[] packet = new byte[1 + 16 + 16 + buffer.Length];
            packet[0] = (byte)MessageType.ReliableData;

            ushort packetSequenceNumber = connection.SequenceNumber;

            // TODO: Remove the unneeded byte[] allocation
            byte[] sqnBytes = BitConverter.GetBytes(connection.SequenceNumber);
            Buffer.BlockCopy(sqnBytes, 0, packet, 1, sqnBytes.Length);

            byte[] sizeBytes = BitConverter.GetBytes((ushort)buffer.Length);
            Buffer.BlockCopy(sizeBytes, 0, packet, 1 + 16, sizeBytes.Length);

            Buffer.BlockCopy(buffer, 0, packet, 1 + 16 + 16, buffer.Length);

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
