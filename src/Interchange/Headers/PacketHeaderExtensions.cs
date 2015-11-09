using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange.Headers
{
    public static class PacketHeaderExtensions
    {
        public static ushort ReadSequenceNumber(this ArraySegment<byte> headerBuffer, int offset) {
            return (ushort)BitConverter.ToInt16(headerBuffer.Array, headerBuffer.Offset + offset);
        }
    }
}
