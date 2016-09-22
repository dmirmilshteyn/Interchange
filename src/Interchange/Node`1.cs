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
#if TEST
        public TestSettings TestSettings { get; }
#endif

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

        public bool IsClient { get; private set; }

        bool disposed;

        Timer updateTimer;

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

#if TEST
        public Node(TestSettings testSettings) : this() {
            this.TestSettings = testSettings;
        }
#endif

        public Node() : this(new NodeConfiguration()) {
        }

        public Node(NodeConfiguration configuration) {
            this.IsClient = false;
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
            updateTimer = new Timer(new TimerCallback(Update), null, 0, 100);
        }

        public Packet RequestNewPacket() {
            var packet = packetPool.GetObject();
            packet.Initialize();
            return packet;
        }

        private void Update(object stateObject) {
            foreach (var connection in connections) {
                connection.Value.Update();
            }
        }

        /// <summary>
        /// Process an incoming message
        /// </summary>
        /// <param name="connection">The originating connection</param>
        /// <param name="packet">The full data packet</param>
        /// <returns>True if the packet has been handled; otherwise, returns false</returns>
        protected virtual bool ProcessIncomingMessageAction(Connection<TTag> connection, Packet packet) {
            return false;
        }

        protected virtual void ProcessConnectionAccepted(Connection<TTag> connection) {
        }

        protected virtual void ProcessConnectionDisconnected(Connection<TTag> connection) {
        }

        private void Close() {
            socket?.Dispose();
        }

        public void ListenAsync(IPAddress localIPAddress, int port) {
            IPEndPoint ipEndPoint = new IPEndPoint(localIPAddress, port);
            socket.Bind(ipEndPoint);

            // Start the listen loop
            PerformReceive();
        }

        public void SendDataAsync(Connection<TTag> connection, byte[] buffer) {
            SendReliableDataPacket(connection, buffer);
        }

        public async Task ConnectAsync(EndPoint endPoint) {
            var connection = new Connection<TTag>(this, endPoint);
            if (!connections.TryAdd(endPoint, connection)) {
                // TODO: Couldn't add the connection
                throw new NotImplementedException();
            }

            this.IsClient = true;

            connectTcs = new TaskCompletionSource<bool>();

            ListenAsync(IPAddress.Any, 0);
            SendInternalPacket(connection, MessageType.Syn);

            await connectTcs.Task;
        }

        public async Task DisconnectAsync(Connection<TTag> connection) {
            if (connection.State != ConnectionState.Connected) {
                throw new NotImplementedException("Invalid remote connection state");
            }

            connection.State = ConnectionState.Disconnecting;
            connection.DisconnectTcs = new TaskCompletionSource<bool>();

            SendClosePacket(connection);
            await connection.DisconnectTcs.Task;

            // TODO: Timeout here to prevent deadlocks
            Connection<TTag> temp;
            while (!connections.TryRemove(connection.RemoteEndPoint, out temp)) { }
        }

        public async Task DisconnectAsync() {
            foreach (var connection in Connections) {
                await DisconnectAsync(connection);
            }
        }

        internal void PerformSend(EndPoint endPoint, Packet packet) {
            try {
                var eventArgs = socketEventArgsPool.GetObject();
                eventArgs.RemoteEndPoint = endPoint;
                eventArgs.SetBuffer(packet.BackingBuffer, packet.Payload.Offset, packet.Payload.Count);
                eventArgs.UserToken = packet;

                bool willRaiseEvent = socket.SendToAsync(eventArgs);
                if (!willRaiseEvent) {
                    HandlePacketSent(eventArgs);

                    socketEventArgsPool.ReleaseObject(eventArgs);
                }
            } catch (ObjectDisposedException) {
                // TODO: Properly dispose of this object
            } catch (System.IO.IOException) {
                // TODO: Properly handle these exceptions
            }
        }

        private void PerformReceive() {
            try {
                var eventArgs = socketEventArgsPool.GetObject();
                var packet = RequestNewPacket();
                eventArgs.SetBuffer(packet.BackingBuffer, 0, packet.BackingBuffer.Length);
                eventArgs.RemoteEndPoint = LocalEndPoint;
                eventArgs.UserToken = packet;

                bool willRaiseEvent = socket.ReceiveFromAsync(eventArgs);
                if (!willRaiseEvent) {
                    HandlePacketReceived(eventArgs);

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

        private void IO_Completed(object sender, SocketAsyncEventArgs e) {
            switch (e.LastOperation) {
                case SocketAsyncOperation.ReceiveFrom:
#if TEST
                    if (TestSettings != null) {
                        bool dropPacket = false;
                        if (TestSettings.PacketDroppingEnabled && TestSettings.PacketDropPercentage > 0) {
                            if (TestSettings.GetNextPacketDropValue() <= TestSettings.PacketDropPercentage) {
                                dropPacket = true;
                            }
                        }

                        if (!dropPacket) {
                            if (TestSettings.SimulatedLatency > 0) {
                                Task.Run(async () =>
                                {
                                    await Task.Delay(TestSettings.SimulatedLatency).ConfigureAwait(false);
                                    HandlePacketReceived(e);

                                    PerformReceive();
                                    socketEventArgsPool.ReleaseObject(e);
                                });

                                return;
                            } else {
                                HandlePacketReceived(e);
                            }
                        }
                    } else {
                        HandlePacketReceived(e);
                    }
#else
                    HandlePacketReceived(e);
#endif

                    PerformReceive();
                    break;
                case SocketAsyncOperation.SendTo:
                    HandlePacketSent(e);
                    break;
                default:
                    throw new NotImplementedException();
            }

            socketEventArgsPool.ReleaseObject(e);
        }

        private void HandlePacketReceived(SocketAsyncEventArgs e) {
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
                                SendAckPacket(connection);

                                connection.State = ConnectionState.Connected;

                                ProcessConnectionAccepted(connection);

                                connectTcs.TrySetResult(true);
                            }
                            break;
                        case MessageType.Ack: {
                                var header = AckHeader.FromSegment(segment);

                                if (header.SequenceNumber == (ushort)(connection.AckNumber - 1) && connection.State == ConnectionState.HandshakeInitiated) {
                                    connection.State = ConnectionState.Connected;

                                    ProcessConnectionAccepted(connection);
                                } else if (connection.State == ConnectionState.Disconnecting && header.SequenceNumber == connection.DisconnectSequenceNumber) {
                                    connection.State = ConnectionState.Disconnected;
                                    ProcessConnectionDisconnected(connection);
                                } else {
                                    // RecordAck will dispose the stored outgoing packet
                                    // Leave this unhandled to allow for the incoming ack packet itself to be disposed
                                    connection.PacketTransmissionController.RecordAck(connection, header.SequenceNumber);
                                }
                                break;
                            }
                        case MessageType.Close: {
                                var header = CloseHeader.FromSegment(segment);
                                SendAckPacket(connection, header.SequenceNumber);

                                if (connection.State == ConnectionState.Connected) {
                                    // This is the receiver - a close request packet is received from the initiator
                                    connection.State = ConnectionState.Disconnecting;
                                    connection.DisconnectSequenceNumber = SendClosePacket(connection);
                                } else if (connection.State == ConnectionState.Disconnecting) {
                                    // This is the initiator - a close confirmation packet is received from the receiver
                                    connection.State = ConnectionState.Disconnected;
                                    if (connection.DisconnectTcs != null) {
                                        connection.DisconnectTcs.TrySetResult(true);
                                    }
                                    ProcessConnectionDisconnected(connection);
                                }

                                break;
                            }
                        case MessageType.ReliableData: {
                                var header = ReliableDataHeader.FromSegment(segment);

                                Packet packet = (Packet)e.UserToken;
                                packet.MarkPayloadRegion(segment.Offset + SystemHeader.Size + 2 + 2, header.PayloadSize);

                                if (systemHeader.TotalFragmentCount > 1) {
                                    var fragmentContainer = connection.RetreivePacketFragmentContainer(header.SequenceNumber, systemHeader.TotalFragmentCount);
                                    bool packetComplete = fragmentContainer.StorePacketFragment(systemHeader.FragmentNumber, packet);

                                    // Always handle this fragment to prevent the packet from being disposed prior to full assembly
                                    handled = true;

                                    if (packetComplete) {
                                        connection.RemovePacketFragmentContainer(header.SequenceNumber);

                                        var fullPacket = new Packet(null, new byte[fragmentContainer.PacketLength]);
                                        int bytesCopied = fragmentContainer.CopyInto(fullPacket.BackingBuffer);
                                        fullPacket.MarkPayloadRegion(0, bytesCopied);

                                        fragmentContainer.Dispose();

                                        if (ProcessIncomingReliableDataPacket(connection, header.SequenceNumber, fullPacket) == false) {
                                            fullPacket.Dispose();
                                        }
                                    }
                                } else {
                                    // This is a full packet
                                    handled = ProcessIncomingReliableDataPacket(connection, header.SequenceNumber, packet);
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

                                    SendSynAckPacket(connection);
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

        private bool ProcessIncomingReliableDataPacket(Connection<TTag> connection, ushort sequenceNumber, Packet packet) {
            bool handled = false;

            if (sequenceNumber == connection.AckNumber) {
                handled = true;

                SendAckPacket(connection);

                bool result = ProcessIncomingMessageAction(connection, packet);
                if (!result) {
                    // Dispose the packet if it has not been handled by user code
                    packet.Dispose();
                }

                foreach (var futurePacket in connection.ReleaseCachedPackets(sequenceNumber)) {
                    // Handle the future packet, but do not send another ack - the ack has already been sent
                    // Instead, only increment the ack number to indicate it was processed
                    connection.IncrementAckNumber();
                    result = ProcessIncomingMessageAction(connection, futurePacket);
                    if (!result) {
                        // Dispose the packet if it has not been handled by user code
                        packet.Dispose();
                    }
                }
            } else if (sequenceNumber < connection.AckNumber) {
                // This is an old, duplicate packet

                // Send the ack, then do nothing else, it has already been handled - drop it
                SendAckPacket(connection, sequenceNumber);
            } else if (sequenceNumber > connection.AckNumber) {
                // This is a future packet, cache it for now
                connection.CachePacket(sequenceNumber, packet);

                // Ack that this packet was received, but don't increment the ack number just yet
                SendAckPacket(connection, sequenceNumber);
            }

            return handled;
        }

        private void HandlePacketSent(SocketAsyncEventArgs e) {
            Packet packet = (Packet)e.UserToken;
            if (packet.CandidateForDisposal) {
                packet.Dispose();
            }
        }

        private void SendToSequenced(Connection<TTag> connection, ushort sequenceNumber, Packet packet) {
            connection.PacketTransmissionController.RecordPacketTransmission(sequenceNumber, connection, packet);

            PerformSend(connection.RemoteEndPoint, packet);
        }

        private void SendInternalPacket(Connection<TTag> connection, MessageType messageType) {
            var packet = RequestNewPacket();

            var systemHeader = new SystemHeader(messageType, 0, 0, 0);

            packet.MarkPayloadRegion(0, SystemHeader.Size + 2);

            systemHeader.WriteTo(packet);

            BitUtility.Write(connection.SequenceNumber, packet.BackingBuffer, SystemHeader.Size);

            connection.IncrementSequenceNumber();

            PerformSend(connection.RemoteEndPoint, packet);
        }

        private void SendSynAckPacket(Connection<TTag> connection) {
            var packet = RequestNewPacket();
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

        private void SendAckPacket(Connection<TTag> connection) {
            SendAckPacket(connection, connection.AckNumber);
            connection.IncrementAckNumber();
        }

        private void SendAckPacket(Connection<TTag> connection, ushort ackNumber) {
            var packet = RequestNewPacket();
            packet.MarkPayloadRegion(0, SystemHeader.Size + 2);

            var systemHeader = new SystemHeader(MessageType.Ack, 0, 0, 0);
            systemHeader.WriteTo(packet);

            BitUtility.Write(ackNumber, packet.BackingBuffer, SystemHeader.Size);

            PerformSend(connection.RemoteEndPoint, packet);
        }

        private void SendReliableDataPacket(Connection<TTag> connection, byte[] buffer) {
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

                SendReliableDataPacket(connection, buffer, sentBytes, length, packetSequenceNumber, i, fragmentCount);

                sentBytes += length;
            }
        }

        private void SendReliableDataPacket(Connection<TTag> connection, byte[] buffer, int bufferOffset, int length, ushort sequenceNumber, byte fragmentNumber, byte totalFragmentCount) {
            var packet = RequestNewPacket();
            packet.MarkPayloadRegion(0, SystemHeader.Size + 2 + 2 + length);

            var systemHeader = new SystemHeader(MessageType.ReliableData, fragmentNumber, totalFragmentCount, 0);
            systemHeader.WriteTo(packet);

            BitUtility.Write(sequenceNumber, packet.BackingBuffer, SystemHeader.Size);
            BitUtility.Write((ushort)length, packet.BackingBuffer, SystemHeader.Size + 2);
            BitUtility.Write(buffer, bufferOffset, packet.BackingBuffer, SystemHeader.Size + 2 + 2, length);

            SendToSequenced(connection, sequenceNumber, packet);
        }

        private ushort SendClosePacket(Connection<TTag> connection) {
            var packet = RequestNewPacket();
            packet.MarkPayloadRegion(0, SystemHeader.Size + 2);

            var systemHeader = new SystemHeader(MessageType.Close, 0, 0, 0);
            systemHeader.WriteTo(packet);

            ushort sequenceNumber = connection.SequenceNumber;
            connection.IncrementSequenceNumber();

            BitUtility.Write(sequenceNumber, packet.BackingBuffer, SystemHeader.Size);

            SendToSequenced(connection, sequenceNumber, packet);

            return sequenceNumber;
        }

        private void ProcessConnectionClose(Connection<TTag> connection) {
        }

        public void Dispose() {
            if (!disposed) {
                disposed = true;
                Close();
            }
        }
    }
}
