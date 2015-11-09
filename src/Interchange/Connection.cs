using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Interchange
{
    public class Connection
    {
        public ConnectionState State { get; internal set; }
        public EndPoint RemoteEndPoint { get; private set; }

        public Connection(EndPoint remoteEndPoint) {
            this.RemoteEndPoint = remoteEndPoint;
        }
    }
}
