using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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

        public Node() {
            buffer = new byte[BufferSize];

            readEventArgs = new SocketAsyncEventArgs();
            readEventArgs.SetBuffer(buffer, 0, buffer.Length);
            readEventArgs.Completed += IO_Completed;
            readEventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

            writeEventArgs = new SocketAsyncEventArgs();
        }

        public async Task ListenAsync(IPAddress localIPAddress, int port) {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            IPEndPoint ipEndPoint = new IPEndPoint(localIPAddress, port);
            socket.Bind(ipEndPoint);

            socket.ReceiveFromAsync(readEventArgs);
        }

        public async Task SendTo(EndPoint endPoint, byte[] buffer) {
            writeEventArgs.RemoteEndPoint = endPoint;
            writeEventArgs.SetBuffer(buffer, 0, buffer.Length);

            PerformSend(writeEventArgs);
        }

        private void PerformSend(SocketAsyncEventArgs e) {
            bool willRaiseEvent = socket.SendToAsync(e);
            if (!willRaiseEvent) {
                HandlePacketSent(e);
            }
        }

        private void PerformReceive(SocketAsyncEventArgs e) {
            bool willRaiseEvent = socket.ReceiveFromAsync(e);
            if (!willRaiseEvent) {
                HandlePacketReceived(e);
            }
        }

        private void IO_Completed(object sender, SocketAsyncEventArgs e) {
            switch (e.LastOperation) {
                case SocketAsyncOperation.ReceiveFrom:
                    HandlePacketReceived(e);
                    break;
                case SocketAsyncOperation.SendTo:
                    HandlePacketSent(e);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private void HandlePacketReceived(SocketAsyncEventArgs e) {
            ArraySegment<byte> segment = new ArraySegment<byte>(e.Buffer, e.Offset, e.BytesTransferred);

            ProcessIncomingMessageAction(segment);

            // Continue listening for new packets
            PerformReceive(e);
        }

        private void HandlePacketSent(SocketAsyncEventArgs e) {

        }
    }
}
