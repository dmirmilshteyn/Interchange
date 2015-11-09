using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange
{
    public struct Packet
    {
        ArraySegment<byte> buffer;
        int sequenceNumber;

    }
}
