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
    public class Node
    {
        public static readonly int BufferSize = 1014;

        Socket socket;

        byte[] buffer;

        SocketAsyncEventArgs readEventArgs;
        SocketAsyncEventArgs writeEventArgs;

        public Action<ArraySegment<byte>> ProcessIncomingMessageAction { get; set; }
        public Action<EndPoint> ProcessConnected { get; set; }

        ConcurrentDictionary<EndPoint, Connection> connections;

        int SequenceNumber;
        int AckNumber;

        TaskCompletionSource<bool> connectTcs;

        PacketTransmissionController packetTransmissionController;

        CancellationToken updateCancellationToken;

        bool client = false;

        public Node() {
            // TODO: Not actually random yet
            Random random = new Random();
            SequenceNumber = 0;//random.Next(ushort.MaxValue, ushort.MaxValue + 1);

            buffer = new byte[BufferSize];

            readEventArgs = new SocketAsyncEventArgs();
            readEventArgs.SetBuffer(buffer, 0, buffer.Length);
            readEventArgs.Completed += IO_Completed;
            readEventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

            writeEventArgs = new SocketAsyncEventArgs();

            connections = new ConcurrentDictionary<EndPoint, Connection>();
            packetTransmissionController = new PacketTransmissionController(this, (ushort)SequenceNumber);

            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            // Begin updating
            updateCancellationToken = new CancellationToken();
            Task.Run(Update, updateCancellationToken);
        }

        private async Task Update() {
            while (true) {
                await packetTransmissionController.ProcessRetransmissions();

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

        public async Task SendTo(EndPoint endPoint, byte[] buffer) {
            writeEventArgs.RemoteEndPoint = endPoint;
            writeEventArgs.SetBuffer(buffer, 0, buffer.Length);

            await PerformSend(writeEventArgs);
        }

        public async Task SendData(EndPoint endPoint, byte[] buffer) {
            await SendReliableDataPacket(endPoint, buffer);
        }

        public async Task Connect(EndPoint endPoint) {
            Connection connection = new Connection(endPoint);
            if (!connections.TryAdd(endPoint, connection)) {
                // TODO: Couldn't add the connection
                throw new NotImplementedException();
            }

            await ListenAsync(IPAddress.Any, 0);
            await SendInternalPacket(endPoint, MessageType.Syn);

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

                                AckNumber = header.SequenceNumber;

                                // Syn-ack received, confirm and establish the connection
                                await SendAckPacket(e.RemoteEndPoint);

                                connection.State = ConnectionState.Connected;
                                if (ProcessConnected != null) {
                                    ProcessConnected(e.RemoteEndPoint);
                                }

                                connectTcs.TrySetResult(true);
                            }
                            break;
                        case MessageType.Ack: {
                                var header = AckHeader.FromSegment(segment);

                                if (header.SequenceNumber == (ushort)(AckNumber - 1)) {
                                    if (ProcessConnected != null) {
                                        ProcessConnected(e.RemoteEndPoint);
                                    }
                                } else {
                                    packetTransmissionController.RecordAck(header.SequenceNumber);
                                }
                                break;
                            }
                        case MessageType.ReliableData: {
                                var header = ReliableDataHeader.FromSegment(segment);

                                ArraySegment<byte> dataBuffer = new ArraySegment<byte>(segment.Array, segment.Offset + 1 + 16 + 16, header.PayloadSize);

                                // TODO: Cache out-of-order packets, release them in order as new packets arrive
                                if (header.SequenceNumber == AckNumber) {
                                    await SendAckPacket(e.RemoteEndPoint);

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

                                AckNumber = header.SequenceNumber;
                                // Received a connection attempt
                                connection = new Connection(e.RemoteEndPoint);
                                if (connections.TryAdd(e.RemoteEndPoint, connection)) {
                                    // TODO: All good, raise events
                                    connection.State = ConnectionState.HandshakeInitiated;
                                    await SendSynAckPacket(e.RemoteEndPoint);

                                    Interlocked.Increment(ref AckNumber);
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

        private async Task SendToSequenced(EndPoint endPoint, ushort sequenceNumber, byte[] buffer) {
            packetTransmissionController.RecordPacketTransmission(sequenceNumber, endPoint, buffer);

            await SendTo(endPoint, buffer);
        }

        private async Task SendInternalPacket(EndPoint endPoint, MessageType messageType) {
            byte[] buffer = new byte[1 + 16];
            buffer[0] = (byte)messageType;

            ushort sequenceNumber = (ushort)SequenceNumber;
            Interlocked.Increment(ref SequenceNumber);

            // TODO: Remove the unneeded byte[] allocation
            byte[] sqnBytes = BitConverter.GetBytes(sequenceNumber);
            Buffer.BlockCopy(sqnBytes, 0, buffer, 1, sqnBytes.Length);

            await SendTo(endPoint, buffer);
        }

        private async Task SendSynAckPacket(EndPoint endPoint) {
            byte[] buffer = new byte[1 + 16 + 16];
            buffer[0] = (byte)MessageType.SynAck;

            ushort sequenceNumber = (ushort)SequenceNumber;
            Interlocked.Increment(ref SequenceNumber);

            // TODO: Remove the unneeded byte[] allocation
            byte[] sqnBytes = BitConverter.GetBytes((ushort)sequenceNumber);
            Buffer.BlockCopy(sqnBytes, 0, buffer, 1, sqnBytes.Length);

            // TODO: Remove the unneeded byte[] allocation
            byte[] ackBytes = BitConverter.GetBytes((ushort)AckNumber);
            Buffer.BlockCopy(ackBytes, 0, buffer, 17, ackBytes.Length);

            //await SendToSequenced(endPoint, sequenceNumber, buffer);
            await SendTo(endPoint, buffer);
        }

        private async Task SendAckPacket(EndPoint endPoint) {
            byte[] buffer = new byte[1 + 16];
            buffer[0] = (byte)MessageType.Ack;

            ushort ackNumber = (ushort)AckNumber;
            Interlocked.Increment(ref AckNumber);

            // TODO: Remove the unneeded byte[] allocation
            byte[] ackBytes = BitConverter.GetBytes(ackNumber);
            Buffer.BlockCopy(ackBytes, 0, buffer, 1, ackBytes.Length);

            await SendTo(endPoint, buffer);
        }

        private async Task SendReliableDataPacket(EndPoint endPoint, byte[] buffer) {
            byte[] packet = new byte[1 + 16 + 16 + buffer.Length];
            packet[0] = (byte)MessageType.ReliableData;

            ushort sequenceNumber = (ushort)SequenceNumber;
            Interlocked.Increment(ref SequenceNumber);

            // TODO: Remove the unneeded byte[] allocation
            byte[] sqnBytes = BitConverter.GetBytes(sequenceNumber);
            Buffer.BlockCopy(sqnBytes, 0, packet, 1, sqnBytes.Length);

            byte[] sizeBytes = BitConverter.GetBytes((ushort)buffer.Length);
            Buffer.BlockCopy(sizeBytes, 0, packet, 1 + 16, sizeBytes.Length);

            Buffer.BlockCopy(buffer, 0, packet, 1 + 16 + 16, buffer.Length);

            await SendToSequenced(endPoint, sequenceNumber, packet);
        }
    }
}
