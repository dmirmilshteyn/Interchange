using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Interchange
{
    public struct PacketTransmissionObject
    {
        public bool Acked;
        public ushort SequenceNumber;
        public EndPoint EndPoint;
        public byte[] Buffer;
        public long LastTransmissionTime;
    }
}
