using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange.Headers
{
    public struct SynAckHeader : IPacketHeader
    {
        public readonly ushort AckNumber;

        private SynAckHeader(ushort ackNumber) {
            this.AckNumber = ackNumber;
        }

        public static SynAckHeader FromSegment(ArraySegment<byte> segment) {
            ushort ackNumber = segment.ReadSequenceNumber(17);

            return new SynAckHeader(ackNumber);
        }
    }
}
