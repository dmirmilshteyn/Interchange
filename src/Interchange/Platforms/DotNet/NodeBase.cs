#if DOTNET

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Interchange
{
    public class NodeBase
    {
        Socket socket;

        public NodeBase() {

        }

        public void Listen(IPAddress localIPAddress, int port) {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            IPEndPoint ipEndPoint = new IPEndPoint(localIPAddress, port);
            socket.Bind(ipEndPoint);
        }
    }
}

#endif