using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange.Headers
{
    public struct ReliableDataHeader : IPacketHeader
    {
        public readonly ushort SequenceNumber;
        public readonly ushort PayloadSize;

        private ReliableDataHeader(ushort sequenceNumber, ushort payloadSize) {
            this.SequenceNumber = sequenceNumber;
            this.PayloadSize = payloadSize;
        }

        public static ReliableDataHeader FromSegment(ArraySegment<byte> segment) {
            ushort sequenceNumber = segment.ReadSequenceNumber(1);
            ushort payloadSize = (ushort)BitConverter.ToInt16(segment.Array, segment.Offset + 1 + 2);

            return new ReliableDataHeader(sequenceNumber, payloadSize);
        }
    }
}
