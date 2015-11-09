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

        public Action<ArraySegment<byte>> ProcessIncomingMessageAction { get; set; }

        public Node() {
            buffer = new byte[BufferSize];

            readEventArgs = new SocketAsyncEventArgs();
            readEventArgs.SetBuffer(buffer, 0, buffer.Length);
            readEventArgs.Completed += ReadEventArgs_Completed;
            readEventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        }

        public async Task ListenAsync(IPAddress localIPAddress, int port) {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            IPEndPoint ipEndPoint = new IPEndPoint(localIPAddress, port);
            socket.Bind(ipEndPoint);

            socket.ReceiveFromAsync(readEventArgs);
        }

        private void PerformReceive(SocketAsyncEventArgs e) {
            bool willRaiseEvent = socket.ReceiveFromAsync(e);
            if (!willRaiseEvent) {
                HandlePacketReceived(e);
            }
        }

        private void ReadEventArgs_Completed(object sender, SocketAsyncEventArgs e) {
            switch (e.LastOperation) {
                case SocketAsyncOperation.ReceiveFrom:
                    HandlePacketReceived(e);
                    PerformReceive(e);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private void HandlePacketReceived(SocketAsyncEventArgs e) {
            ArraySegment<byte> segment = new ArraySegment<byte>(e.Buffer, e.Offset, e.BytesTransferred);

            ProcessIncomingMessageAction(segment);
        }
    }
}
