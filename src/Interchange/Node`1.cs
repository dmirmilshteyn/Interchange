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

        ConcurrentDictionary<EndPoint, Connection<TTag>> connections;
        NodeConfiguration configuration;

        TaskCompletionSource<bool> connectTcs;

        CancellationToken updateCancellationToken;

        bool client = false;
        bool disposed;

        public Connection<TTag> RemoteConnection {
            get {
                if (connections.Count > 0) {
                    return connections.First().Value;
                } else {
                    return null;
                }
            }
        }

        public IEnumerable<Connection<TTag>> Connections {
            get {
                foreach (var connection in connections) {
                    yield return connection.Value;
                }
            }
        }

        public Node() : this(new NodeConfiguration()) {
        }

        public Node(NodeConfiguration configuration) {
            this.configuration = configuration;

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
                    connection.Value.Update();
                }

                await UserUpdate();
            }
        }

        protected virtual async Task UserUpdate() {
            // Delay to prevent maxing CPU
            // This behavious can be overwritten by not calling this base function
            await Task.Delay(1);
        }

        /// <summary>
        /// Process an incoming message
        /// </summary>
        /// <param name="connection">The originating connection</param>
        /// <param name="packet">The full data packet</param>
        /// <returns>True if the packet has been handled; otherwise, returns false</returns>
        protected virtual Task<bool> ProcessIncomingMessageAction(Connection<TTag> connection, Packet packet) {
            return Task.FromResult(false);
        }

        protected virtual Task ProcessConnectionAccepted(Connection<TTag> connection) {
            return TaskInterop.CompletedTask;
        }

        private void Close() {
            socket?.Dispose();
        }

        public Task ListenAsync(IPAddress localIPAddress, int port) {
            IPEndPoint ipEndPoint = new IPEndPoint(localIPAddress, port);
            socket.Bind(ipEndPoint);

            // Start listening, but do not wait for it to complete before returning
            PerformReceive();

            return TaskInterop.CompletedTask;
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

        internal async void PerformSend(EndPoint endPoint, Packet packet) {
            try {
                // Needs to be an async-void to prevent blocking
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

        private async void PerformReceive() {
            try {
                // Needs to be an async-void because it goes through the IO_Completed loop, which doesn't support tasks
                var eventArgs = socketEventArgsPool.GetObject();
                var packet = await RequestNewPacket();
                eventArgs.SetBuffer(packet.BackingBuffer, 0, packet.BackingBuffer.Length);
                eventArgs.RemoteEndPoint = LocalEndPoint;
                eventArgs.UserToken = packet;

                bool willRaiseEvent = socket.ReceiveFromAsync(eventArgs);
                if (!willRaiseEvent) {
                    await HandlePacketReceived(eventArgs);

                    socketEventArgsPool.ReleaseObject(eventArgs);

                    // Continue listening for new packets
                    PerformReceive();
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

                    PerformReceive();
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
                var systemHeader = SystemHeader.FromSegment(segment);

                Connection<TTag> connection;
                if (connections.TryGetValue(e.RemoteEndPoint, out connection)) {
                    switch (systemHeader.MessageType) {
                        case MessageType.Syn:
                            // TODO: Reject the connection, already connected!
                            throw new NotImplementedException();
                        case MessageType.SynAck: {
                                var header = SynAckHeader.FromSegment(segment);

                                connection.InitializeAckNumber(header.SequenceNumber);

                                // Syn-ack received, confirm and establish the connection
                                await SendAckPacket(connection);

                                connection.State = ConnectionState.Connected;

                                await ProcessConnectionAccepted(connection);

                                connectTcs.TrySetResult(true);
                            }
                            break;
                        case MessageType.Ack: {
                                var header = AckHeader.FromSegment(segment);

                                if (header.SequenceNumber == (ushort)(connection.AckNumber - 1) && connection.State == ConnectionState.HandshakeInitiated) {
                                    connection.State = ConnectionState.Connected;

                                    await ProcessConnectionAccepted(connection);
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
                                packet.MarkPayloadRegion(segment.Offset + 1 + 2 + 2, header.PayloadSize);

                                if (header.SequenceNumber == connection.AckNumber) {
                                    handled = true;

                                    await SendAckPacket(connection);

                                    bool result = await ProcessIncomingMessageAction(connection, packet);
                                    if (!result) {
                                        // Dispose the packet if it has not been handled by user code
                                        packet.Dispose();
                                    }

                                    foreach (var futurePacket in connection.ReleaseCachedPackets(header.SequenceNumber)) {
                                        // Handle the future packet, but do not send another ack - the ack has already been sent
                                        // Instead, only increment the ack number to indicate it was processed
                                        connection.IncrementAckNumber();
                                        result = await ProcessIncomingMessageAction(connection, futurePacket);
                                        if (!result) {
                                            // Dispose the packet if it has not been handled by user code
                                            packet.Dispose();
                                        }
                                    }
                                } else if (header.SequenceNumber < connection.AckNumber) {
                                    // This is an old, duplicate packet

                                    // Send the ack, then do nothing else, it has already been handled - drop it
                                    await SendAckPacket(connection, header.SequenceNumber);
                                } else if (header.SequenceNumber > connection.AckNumber) {
                                    // This is a future packet, cache it for now
                                    connection.CachePacket(header.SequenceNumber, packet);

                                    // Ack that this packet was received, but don't increment the ack number just yet
                                    await SendAckPacket(connection, header.SequenceNumber);
                                }
                                break;
                            }
                    }
                } else {
                    switch (systemHeader.MessageType) {
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
        }

        private Task HandlePacketSent(SocketAsyncEventArgs e) {
            Packet packet = (Packet)e.UserToken;
            if (packet.CandidateForDisposal) {
                packet.Dispose();
            }

            return TaskInterop.CompletedTask;
        }

        private Task SendToSequenced(Connection<TTag> connection, ushort sequenceNumber, Packet packet) {
            connection.PacketTransmissionController.RecordPacketTransmission(sequenceNumber, connection, packet);

            PerformSend(connection.RemoteEndPoint, packet);

            return TaskInterop.CompletedTask;
        }

        private async Task SendInternalPacket(Connection<TTag> connection, MessageType messageType) {
            var packet = await RequestNewPacket();

            var systemHeader = new SystemHeader(messageType, 0, 0, 0);

            packet.MarkPayloadRegion(0, SystemHeader.Size + 2);

            systemHeader.WriteTo(packet);

            BitUtility.Write(connection.SequenceNumber, packet.BackingBuffer, SystemHeader.Size);

            connection.IncrementSequenceNumber();

            PerformSend(connection.RemoteEndPoint, packet);
        }

        private async Task SendSynAckPacket(Connection<TTag> connection) {
            var packet = await RequestNewPacket();
            packet.MarkPayloadRegion(0, SystemHeader.Size + 2 + 2);

            var systemHeader = new SystemHeader(MessageType.SynAck, 0, 0, 0);
            systemHeader.WriteTo(packet);

            BitUtility.Write(connection.SequenceNumber, packet.BackingBuffer, SystemHeader.Size);
            BitUtility.Write(connection.AckNumber, packet.BackingBuffer, SystemHeader.Size + 2);

            connection.IncrementSequenceNumber();
            connection.IncrementAckNumber();

            //await SendToSequenced(endPoint, sequenceNumber, buffer);
            PerformSend(connection.RemoteEndPoint, packet);
        }

        private async Task SendAckPacket(Connection<TTag> connection) {
            await SendAckPacket(connection, connection.AckNumber);
            connection.IncrementAckNumber();

        }

        private async Task SendAckPacket(Connection<TTag> connection, ushort ackNumber) {
            var packet = await RequestNewPacket();
            packet.MarkPayloadRegion(0, SystemHeader.Size + 2);

            var systemHeader = new SystemHeader(MessageType.Ack, 0, 0, 0);
            systemHeader.WriteTo(packet);

            BitUtility.Write(ackNumber, packet.BackingBuffer, SystemHeader.Size);

            PerformSend(connection.RemoteEndPoint, packet);
        }

        private async Task SendReliableDataPacket(Connection<TTag> connection, byte[] buffer) {
            int payloadFragmentSize = configuration.MTU - (SystemHeader.Size + 2 + 2);

            byte fragmentCount = (byte)Math.Ceiling(buffer.Length / (double)payloadFragmentSize);

            ushort packetSequenceNumber = connection.SequenceNumber;
            connection.IncrementSequenceNumber();

            int sentBytes = 0;
            for (byte i = 0; i < fragmentCount; i++) {
                int length = payloadFragmentSize;
                if (sentBytes + payloadFragmentSize > buffer.Length) {
                    // This is the remainder of the packet
                    length = buffer.Length - sentBytes;
                }

                await SendReliableDataPacket(connection, buffer, sentBytes, length, packetSequenceNumber, i, fragmentCount);

                sentBytes += length;
            }
        }

        private async Task SendReliableDataPacket(Connection<TTag> connection, byte[] buffer, int bufferOffset, int length, ushort sequenceNumber, byte fragmentNumber, byte totalFragmentCount) {
            var packet = await RequestNewPacket();
            packet.MarkPayloadRegion(0, SystemHeader.Size + 2 + 2 + length);

            var systemHeader = new SystemHeader(MessageType.ReliableData, fragmentNumber, totalFragmentCount, 0);
            systemHeader.WriteTo(packet);

            BitUtility.Write(sequenceNumber, packet.BackingBuffer, SystemHeader.Size);
            BitUtility.Write((ushort)length, packet.BackingBuffer, SystemHeader.Size + 2);
            BitUtility.Write(buffer, bufferOffset, packet.BackingBuffer, SystemHeader.Size + 2 + 2, length);

            await SendToSequenced(connection, sequenceNumber, packet);
        }

        public void Dispose() {
            if (!disposed) {
                disposed = true;
                Close();
            }
        }
    }
}
