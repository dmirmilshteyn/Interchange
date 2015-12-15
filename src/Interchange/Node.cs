using Interchange.Headers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Interchange
{
    public class Node : IDisposable
    {
        public static readonly int BufferSize = 1014;

        Socket socket;

        byte[] buffer;

        SocketAsyncEventArgs readEventArgs;
        SocketAsyncEventArgs writeEventArgs;

        public Action<ArraySegment<byte>> ProcessIncomingMessageAction { get; set; }
        public Action<EndPoint> ProcessConnected { get; set; }

        ConcurrentDictionary<EndPoint, Connection> connections;

        TaskCompletionSource<bool> connectTcs;

        CancellationToken updateCancellationToken;

        bool client = false;
        bool disposed;

        public Connection RemoteConnection {
            get {
                return connections.First().Value;
            }
        }

        public Node() {
            // TODO: Not actually random yet
            Random random = new Random();

            buffer = new byte[BufferSize];

            readEventArgs = new SocketAsyncEventArgs();
            readEventArgs.SetBuffer(buffer, 0, buffer.Length);
            readEventArgs.Completed += IO_Completed;
            readEventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

            writeEventArgs = new SocketAsyncEventArgs();

            connections = new ConcurrentDictionary<EndPoint, Connection>();
            

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

        public void Close() {
            socket?.Dispose();
        }

        public async Task ListenAsync(IPAddress localIPAddress, int port) {
            IPEndPoint ipEndPoint = new IPEndPoint(localIPAddress, port);
            socket.Bind(ipEndPoint);

            socket.ReceiveFromAsync(readEventArgs);
        }

        internal async Task SendTo(EndPoint endPoint, byte[] buffer) {
            writeEventArgs.RemoteEndPoint = endPoint;
            writeEventArgs.SetBuffer(buffer, 0, buffer.Length);

            await PerformSend(writeEventArgs);
        }

        public async Task SendDataAsync(Connection connection, byte[] buffer) {
            await SendReliableDataPacket(connection, buffer);
        }

        public async Task ConnectAsync(EndPoint endPoint) {
            Connection connection = new Connection(this, endPoint);
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

        private async Task PerformSend(SocketAsyncEventArgs e) {
            try {
                bool willRaiseEvent = socket.SendToAsync(e);
                if (!willRaiseEvent) {
                    await HandlePacketSent(e);
                }
            } catch {
                // TODO: Properly handle these exceptions
            }
        }

        private async Task PerformReceive(SocketAsyncEventArgs e) {
            try {
                bool willRaiseEvent = socket.ReceiveFromAsync(e);
                if (!willRaiseEvent) {
                    await HandlePacketReceived(e);
                }
            } catch {
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
        }

        private async Task HandlePacketReceived(SocketAsyncEventArgs e) {
            ArraySegment<byte> segment = new ArraySegment<byte>(e.Buffer, e.Offset, e.BytesTransferred);

            if (segment.Count > 0) {
                MessageType messageType = (MessageType)segment.Array[segment.Offset];

                Connection connection;
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
                                connection = new Connection(this, e.RemoteEndPoint);
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

            // Continue listening for new packets
            await PerformReceive(e);
        }

        private async Task HandlePacketSent(SocketAsyncEventArgs e) {

        }

        private async Task SendToSequenced(Connection connection, ushort sequenceNumber, byte[] buffer) {
            connection.PacketTransmissionController.RecordPacketTransmission(sequenceNumber, connection, buffer);

            await SendTo(connection.RemoteEndPoint, buffer);
        }

        private async Task SendInternalPacket(Connection connection, MessageType messageType) {
            byte[] buffer = new byte[1 + 16];
            buffer[0] = (byte)messageType;

            // TODO: Remove the unneeded byte[] allocation
            byte[] sqnBytes = BitConverter.GetBytes(connection.SequenceNumber);
            Buffer.BlockCopy(sqnBytes, 0, buffer, 1, sqnBytes.Length);

            connection.IncrementSequenceNumber();

            await SendTo(connection.RemoteEndPoint, buffer);
        }

        private async Task SendSynAckPacket(Connection connection) {
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
            await SendTo(connection.RemoteEndPoint, buffer);
        }

        private async Task SendAckPacket(Connection connection) {
            byte[] buffer = new byte[1 + 16];
            buffer[0] = (byte)MessageType.Ack;

            // TODO: Remove the unneeded byte[] allocation
            byte[] ackBytes = BitConverter.GetBytes(connection.AckNumber);
            Buffer.BlockCopy(ackBytes, 0, buffer, 1, ackBytes.Length);

            connection.IncrementAckNumber();

            await SendTo(connection.RemoteEndPoint, buffer);
        }

        private async Task SendReliableDataPacket(Connection connection, byte[] buffer) {
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
