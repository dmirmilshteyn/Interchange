﻿using Interchange.Headers;
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

        public Node() {
            // TODO: Not actually random yet
            Random random = new Random();
            SequenceNumber = random.Next(0, ushort.MaxValue);

            buffer = new byte[BufferSize];

            readEventArgs = new SocketAsyncEventArgs();
            readEventArgs.SetBuffer(buffer, 0, buffer.Length);
            readEventArgs.Completed += IO_Completed;
            readEventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

            writeEventArgs = new SocketAsyncEventArgs();

            connections = new ConcurrentDictionary<EndPoint, Connection>();

            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
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

            connectTcs = new TaskCompletionSource<bool>();

            await connectTcs.Task;
        }

        private async Task PerformSend(SocketAsyncEventArgs e) {
            bool willRaiseEvent = socket.SendToAsync(e);
            if (!willRaiseEvent) {
                await HandlePacketSent(e);
            }
        }

        private async Task PerformReceive(SocketAsyncEventArgs e) {
            bool willRaiseEvent = socket.ReceiveFromAsync(e);
            if (!willRaiseEvent) {
                await HandlePacketReceived(e);
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
                        case MessageType.SynAck:
                            {
                                var header = SynAckHeader.FromSegment(segment);

                                AckNumber = header.AckNumber;
                                Interlocked.Increment(ref AckNumber);

                                // Syn-ack received, confirm and establish the connection
                                await SendAckPacket(e.RemoteEndPoint);

                                connection.State = ConnectionState.Connected;
                                if (ProcessConnected != null) {
                                    ProcessConnected(e.RemoteEndPoint);
                                }

                                connectTcs.TrySetResult(true);
                            }
                            break;
                        case MessageType.Ack:
                            {
                                var header = AckHeader.FromSegment(segment);

                                if (header.SequenceNumber == AckNumber + 1) {
                                    if (ProcessConnected != null) {
                                        ProcessConnected(e.RemoteEndPoint);
                                    }
                                }
                                break;
                            }
                        case MessageType.ReliableData:
                            {
                                var header = ReliableDataHeader.FromSegment(segment);

                                ArraySegment<byte> dataBuffer = new ArraySegment<byte>(segment.Array, segment.Offset + 1 + 16 + 16, header.PayloadSize);
                                if (ProcessIncomingMessageAction != null) {
                                    ProcessIncomingMessageAction(dataBuffer);
                                }
                                break;
                            }
                    }
                } else {
                    switch (messageType) {
                        case MessageType.Syn:
                            {
                                var header = SynHeader.FromSegment(segment);

                                AckNumber = header.SequenceNumber;
                                Interlocked.Increment(ref AckNumber);
                                // Received a connection attempt
                                connection = new Connection(e.RemoteEndPoint);
                                if (connections.TryAdd(e.RemoteEndPoint, connection)) {
                                    // TODO: All good, raise events
                                    connection.State = ConnectionState.HandshakeInitiated;
                                    await SendSynAckPacket(e.RemoteEndPoint);
                                } else {
                                    // Couldn't add to the connections dictionary - uh oh!
                                    throw new NotImplementedException();
                                }
                                break;
                            }
                        case MessageType.SynAck:
                            {
                                // TODO: Got a synack, but no local connection has been initiated
                                throw new NotImplementedException();
                            }
                    }
                }
            }

            if (ProcessIncomingMessageAction != null) {
                ProcessIncomingMessageAction(segment);
            }

            // Continue listening for new packets
            await PerformReceive(e);
        }

        private async Task HandlePacketSent(SocketAsyncEventArgs e) {

        }

        private async Task SendInternalPacket(EndPoint endPoint, MessageType messageType) {
            byte[] buffer = new byte[1 + 16];
            buffer[0] = (byte)messageType;

            // TODO: Remove the unneeded byte[] allocation
            byte[] sqnBytes = BitConverter.GetBytes((ushort)SequenceNumber);
            Buffer.BlockCopy(sqnBytes, 0, buffer, 1, sqnBytes.Length);
            Interlocked.Increment(ref SequenceNumber);

            await SendTo(endPoint, buffer);
        }

        private async Task SendSynAckPacket(EndPoint endPoint) {
            byte[] buffer = new byte[1 + 16 + 16];
            buffer[0] = (byte)MessageType.SynAck;

            // TODO: Remove the unneeded byte[] allocation
            byte[] sqnBytes = BitConverter.GetBytes((ushort)SequenceNumber);
            Buffer.BlockCopy(sqnBytes, 0, buffer, 1, sqnBytes.Length);
            Interlocked.Increment(ref SequenceNumber);

            // TODO: Remove the unneeded byte[] allocation
            byte[] ackBytes = BitConverter.GetBytes((ushort)AckNumber);
            Buffer.BlockCopy(ackBytes, 0, buffer, 17, ackBytes.Length);

            await SendTo(endPoint, buffer);
        }

        private async Task SendAckPacket(EndPoint endPoint) {
            byte[] buffer = new byte[1 + 16];
            buffer[0] = (byte)MessageType.Ack;

            // TODO: Remove the unneeded byte[] allocation
            byte[] ackBytes = BitConverter.GetBytes((ushort)AckNumber);
            Buffer.BlockCopy(ackBytes, 0, buffer, 1, ackBytes.Length);
            Interlocked.Increment(ref AckNumber);

            await SendTo(endPoint, buffer);
        }

        private async Task SendReliableDataPacket(EndPoint endPoint, byte[] buffer) {
            byte[] packet = new byte[1 + 16 + 16 + buffer.Length];
            packet[0] = (byte)MessageType.ReliableData;

            // TODO: Remove the unneeded byte[] allocation
            byte[] sqnBytes = BitConverter.GetBytes((ushort)SequenceNumber);
            Buffer.BlockCopy(sqnBytes, 0, packet, 1, sqnBytes.Length);
            Interlocked.Increment(ref SequenceNumber);

            byte[] sizeBytes = BitConverter.GetBytes((ushort)buffer.Length);
            Buffer.BlockCopy(sizeBytes, 0, packet, 1 + 16, sizeBytes.Length);

            Buffer.BlockCopy(buffer, 0, packet, 1 + 16 + 16, buffer.Length);

            await SendTo(endPoint, packet);
        }
    }
}
