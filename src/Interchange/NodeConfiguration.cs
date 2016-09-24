using System;
using System.Collections.Generic;
using System.Linq;

namespace Interchange
{
    public class NodeConfiguration
    {
        public int MTU { get; set; } = 1024;
        public int BufferPoolSize { get; set; } = 32;
        public int SocketEventPoolSize { get; set; } = 32;

        public NodeConfiguration() {
        }
    }
}
