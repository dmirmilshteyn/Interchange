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
            if (!TryAddConnection(endPoint, connection)) {
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
        }

        public async Task DisconnectAsync() {
            foreach (var connection in Connections) {
                await DisconnectAsync(connection);
            }
        }

        private void BindEvents(Connection<TTag> connection) {
            connection.ConnectionLost += Connection_ConnectionLost;
        }

        private void UnbindEvents(Connection<TTag> connection) {
            connection.ConnectionLost -= Connection_ConnectionLost;
        }

        private bool TryAddConnection(EndPoint endPoint, Connection<TTag> connection) {
            if (connections.TryAdd(endPoint, connection)) {
                BindEvents(connection);

                return true;
            }

            return false;
        }

        private void RemoveConnection(Connection<TTag> connection) {
            UnbindEvents(connection);

            // TODO: Timeout here to prevent deadlocks
            Connection<TTag> temp;
            while (!connections.TryRemove(connection.RemoteEndPoint, out temp)) { }

            ProcessConnectionDisconnected(connection);
        }

        private void Connection_ConnectionLost(object sender, EventArgs e) {
            RemoveConnection((Connection<TTag>)sender);
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
                    HandleReceiveIOCompleted(eventArgs);
                }
            } catch (ObjectDisposedException) {
                // TODO: Properly dispose of this object
            } catch (System.IO.IOException) {
                // TODO: Properly handle these exceptions
            }
        }

        private void HandleReceiveIOCompleted(SocketAsyncEventArgs e) {
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

            socketEventArgsPool.ReleaseObject(e);
        }

        private void IO_Completed(object sender, SocketAsyncEventArgs e) {
            switch (e.LastOperation) {
                case SocketAsyncOperation.ReceiveFrom:
                    HandleReceiveIOCompleted(e);
                    break;
                case SocketAsyncOperation.SendTo:
                    HandlePacketSent(e);
                    socketEventArgsPool.ReleaseObject(e);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private void HandlePacketReceived(SocketAsyncEventArgs e) {
            ArraySegment<byte> segment = new ArraySegment<byte>(e.Buffer, e.Offset, e.BytesTransferred);

            bool handled = false;

            if (segment.Count > 0) {
                var systemHeader = SystemHeader.FromSegment(segment);

                Connection<TTag> connection;
                if (connections.TryGetValue(e.RemoteEndPoint, out connection)) {
                    lock (connection) {
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
                                        RemoveConnection(connection);
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
                                        connection.TriggerConnectionLost();
                                    }

                                    break;
                                }
                            case MessageType.Heartbeat: {
                                    var header = HeartbeatHeader.FromSegment(segment);

                                    var packet = (Packet)e.UserToken;

                                    handled = ProcessIncomingReliableDataPacket(connection, header.SequenceNumber, packet, false);
                                }
                                break;
                            case MessageType.FragmentedReliableData: {
                                    var header = FragmentedReliableDataHeader.FromSegment(segment);

                                    var packet = (Packet)e.UserToken;
                                    packet.MarkPayloadRegion(segment.Offset + SystemHeader.Size + FragmentedReliableDataHeader.Size, header.PayloadSize);

                                    handled = ProcessIncomingReliableDataPacket(connection, header.SequenceNumber, packet, true, header.TotalFragmentCount);
                                }
                                break;
                            case MessageType.ReliableData: {
                                    var header = ReliableDataHeader.FromSegment(segment);

                                    Packet packet = (Packet)e.UserToken;
                                    packet.MarkPayloadRegion(segment.Offset + SystemHeader.Size + ReliableDataHeader.Size, header.PayloadSize);

                                    handled = ProcessIncomingReliableDataPacket(connection, header.SequenceNumber, packet, true);
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
                                if (TryAddConnection(e.RemoteEndPoint, connection)) {
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

        private bool ProcessIncomingReliableDataPacket(Connection<TTag> connection, ushort sequenceNumber, Packet packet, bool publishMessage, ushort totalFragmentCount = 0) {
            bool handled = false;

            if (sequenceNumber == connection.AckNumber) {
                handled = true;

                SendAckPacket(connection);

                ProcessReliableDataPacket(connection, packet, sequenceNumber, totalFragmentCount, publishMessage);

                foreach (var futurePacket in connection.ReleaseCachedPackets(sequenceNumber)) {
                    // Handle the future packet, but do not send another ack - the ack has already been sent
                    // Instead, only increment the ack number to indicate it was processed
                    connection.IncrementAckNumber();

                    ProcessReliableDataPacket(connection, futurePacket.Packet, futurePacket.SequenceNumber, futurePacket.TotalFragmentCount, futurePacket.PublishMessage);
                }
            } else if (sequenceNumber < connection.AckNumber) {
                // This is an old, duplicate packet

                // Send the ack, then do nothing else, it has already been handled - drop it
                SendAckPacket(connection, sequenceNumber);
            } else if (sequenceNumber > connection.AckNumber) {
                // This is a future packet, cache it for now
                connection.CachePacket(sequenceNumber, new CachedPacketInformation(packet, sequenceNumber, totalFragmentCount, publishMessage));

                // Ack that this packet was received, but don't increment the ack number just yet
                SendAckPacket(connection, sequenceNumber);

                // Prevent the packet from being disposed immediately
                handled = true;
            }

            return handled;
        }

        private void ProcessReliableDataPacket(Connection<TTag> connection, Packet packet, ushort sequenceNumber, int totalFragmentCount, bool publishMessage) {
            // If there is only 1 fragment, it can be treated as a standalone packet
            if (totalFragmentCount > 1) {
                if (connection.ActiveFragmentContainer != null) {
                    throw new NotImplementedException("ActiveFragmentContainer is already active - malformed packet?");
                }

                connection.ActiveFragmentContainer = new PacketFragmentContainer(sequenceNumber, totalFragmentCount);
                connection.ActiveFragmentContainer.AddPacketFragment(packet);
            } else {
                if (connection.ActiveFragmentContainer == null) {
                    bool handled = false;
                    if (publishMessage) {
                        handled = ProcessIncomingMessageAction(connection, packet);
                    }
                    if (!handled) {
                        // Dispose the packet if it has not been handled by user code
                        packet.Dispose();
                    }
                } else {
                    // Only include fragments that can be published
                    if (publishMessage) {
                        var packetComplete = connection.ActiveFragmentContainer.AddPacketFragment(packet);

                        if (packetComplete) {
                            var fragmentContainer = connection.ActiveFragmentContainer;
                            connection.ActiveFragmentContainer = null;

                            var fullPacket = new Packet(null, new byte[fragmentContainer.PacketLength]);
                            int bytesCopied = fragmentContainer.CopyInto(fullPacket.BackingBuffer);
                            fullPacket.MarkPayloadRegion(0, bytesCopied);

                            fragmentContainer.Dispose();

                            bool handled = false;
                            if (publishMessage) {
                                handled = ProcessIncomingMessageAction(connection, fullPacket);
                            }
                            if (!handled) {
                                // Dispose the packet if it has not been handled by user code
                                fullPacket.Dispose();
                            }
                        }
                    }
                }
            }
        }

        private void HandlePacketSent(SocketAsyncEventArgs e) {
            Packet packet = (Packet)e.UserToken;
            if (packet.CandidateForDisposal) {
                packet.Dispose();
            }
        }

        private void SendToSequenced(Connection<TTag> connection, ushort sequenceNumber, Packet packet) {
            connection.PacketTransmissionController.RecordPacketTransmissionAndEnqueue(sequenceNumber, connection, packet);

            PerformSend(connection.RemoteEndPoint, packet);
        }

        private void SendInternalPacket(Connection<TTag> connection, MessageType messageType) {
            lock (connection) {
                var packet = RequestNewPacket();

                var systemHeader = new SystemHeader(messageType, 0);

                packet.MarkPayloadRegion(0, SystemHeader.Size + 2);

                systemHeader.WriteTo(packet);

                BitUtility.Write(connection.SequenceNumber, packet.BackingBuffer, SystemHeader.Size);

                connection.IncrementSequenceNumber();

                PerformSend(connection.RemoteEndPoint, packet);
            }
        }

        private void SendSynAckPacket(Connection<TTag> connection) {
            lock (connection) {
                var packet = RequestNewPacket();
                packet.MarkPayloadRegion(0, SystemHeader.Size + 2 + 2);

                var systemHeader = new SystemHeader(MessageType.SynAck, 0);
                systemHeader.WriteTo(packet);

                BitUtility.Write(connection.SequenceNumber, packet.BackingBuffer, SystemHeader.Size);
                BitUtility.Write(connection.AckNumber, packet.BackingBuffer, SystemHeader.Size + 2);

                connection.IncrementSequenceNumber();
                connection.IncrementAckNumber();

                //await SendToSequenced(endPoint, sequenceNumber, buffer);
                PerformSend(connection.RemoteEndPoint, packet);
            }
        }

        private void SendAckPacket(Connection<TTag> connection) {
            lock (connection) {
                SendAckPacket(connection, connection.AckNumber);
                connection.IncrementAckNumber();
            }
        }

        private void SendAckPacket(Connection<TTag> connection, ushort ackNumber) {
            var packet = RequestNewPacket();
            packet.MarkPayloadRegion(0, SystemHeader.Size + 2);

            var systemHeader = new SystemHeader(MessageType.Ack, 0);
            systemHeader.WriteTo(packet);

            BitUtility.Write(ackNumber, packet.BackingBuffer, SystemHeader.Size);

            PerformSend(connection.RemoteEndPoint, packet);
        }

        private void SendReliableDataPacket(Connection<TTag> connection, byte[] buffer) {
            lock (connection) {
                int initialPayloadFragmentSize = configuration.MTU - (SystemHeader.Size + FragmentedReliableDataHeader.Size);
                int followingFragmentsSize = configuration.MTU - (SystemHeader.Size + ReliableDataHeader.Size);

                ushort fragmentCount = 0;
                if (buffer.Length > followingFragmentsSize) { // If the payload can't fit in a regular reliable data packet, fragment it
                                                              // Include the initial fragment header packet as part of the count
                    fragmentCount++;
                    fragmentCount += (ushort)Math.Ceiling((buffer.Length - initialPayloadFragmentSize) / (double)followingFragmentsSize);
                }

                int sentBytes = 0;
                ushort packetSequenceNumber = 0;

                if (fragmentCount > 0) {
                    // Send the initial packet with fragment information
                    int length = initialPayloadFragmentSize;
                    if (initialPayloadFragmentSize > buffer.Length) {
                        length = buffer.Length;
                    }

                    packetSequenceNumber = connection.SequenceNumber;
                    connection.IncrementSequenceNumber();

                    SendFragmentedReliableDataPacket(connection, buffer, sentBytes, length, packetSequenceNumber, fragmentCount);

                    sentBytes += length;
                }

                while (sentBytes < buffer.Length) {
                    int length = followingFragmentsSize;
                    if (sentBytes + followingFragmentsSize > buffer.Length) {
                        // This is the remainder of the packet
                        length = buffer.Length - sentBytes;
                    }

                    packetSequenceNumber = connection.SequenceNumber;
                    connection.IncrementSequenceNumber();

                    SendReliableDataPacket(connection, buffer, sentBytes, length, packetSequenceNumber);

                    sentBytes += length;
                }
            }
        }

        private void SendFragmentedReliableDataPacket(Connection<TTag> connection, byte[] buffer, int bufferOffset, int length, ushort sequenceNumber, ushort totalFragmentCount) {
                var packet = RequestNewPacket();
                packet.MarkPayloadRegion(0, SystemHeader.Size + FragmentedReliableDataHeader.Size + length);

                var systemHeader = new SystemHeader(MessageType.FragmentedReliableData, 0);
                systemHeader.WriteTo(packet);

                var fragmentedReliableDataHeader = new FragmentedReliableDataHeader(sequenceNumber, (ushort)length, totalFragmentCount);
                fragmentedReliableDataHeader.WriteTo(packet.BackingBuffer, SystemHeader.Size);

                BitUtility.Write(buffer, bufferOffset, packet.BackingBuffer, SystemHeader.Size + FragmentedReliableDataHeader.Size, length);

                SendToSequenced(connection, sequenceNumber, packet);
        }

        private void SendReliableDataPacket(Connection<TTag> connection, byte[] buffer, int bufferOffset, int length, ushort sequenceNumber) {
            var packet = RequestNewPacket();
            packet.MarkPayloadRegion(0, SystemHeader.Size + ReliableDataHeader.Size + length);

            var systemHeader = new SystemHeader(MessageType.ReliableData, 0);
            systemHeader.WriteTo(packet);

            var reliableDataHeader = new ReliableDataHeader(sequenceNumber, (ushort)length);
            reliableDataHeader.WriteTo(packet.BackingBuffer, SystemHeader.Size);

            BitUtility.Write(buffer, bufferOffset, packet.BackingBuffer, SystemHeader.Size + ReliableDataHeader.Size, length);

            SendToSequenced(connection, sequenceNumber, packet);
        }

        internal void SendHeartbeatPacket(Connection<TTag> connection, ushort sequenceNumber) {
            var packet = RequestNewPacket();

            var systemHeader = new SystemHeader(MessageType.Heartbeat, 0);
            systemHeader.WriteTo(packet);

            var heartbeatHeader = new HeartbeatHeader(sequenceNumber);
            heartbeatHeader.WriteTo(packet.BackingBuffer, SystemHeader.Size);

            SendToSequenced(connection, sequenceNumber, packet);
        }

        private ushort SendClosePacket(Connection<TTag> connection) {
            lock (connection) {
                var packet = RequestNewPacket();
                packet.MarkPayloadRegion(0, SystemHeader.Size + 2);

                var systemHeader = new SystemHeader(MessageType.Close, 0);
                systemHeader.WriteTo(packet);

                ushort sequenceNumber = connection.SequenceNumber;
                connection.IncrementSequenceNumber();

                BitUtility.Write(sequenceNumber, packet.BackingBuffer, SystemHeader.Size);

                SendToSequenced(connection, sequenceNumber, packet);

                return sequenceNumber;
            }
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
