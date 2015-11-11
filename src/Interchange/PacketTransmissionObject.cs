using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange
{
    public struct PacketTransmissionObject
    {
        public bool Acked;
        public int SequenceNumber;
        public byte[] Buffer;
        public long LastTransmissionTime;
    }
}
