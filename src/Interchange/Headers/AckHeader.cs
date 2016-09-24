using System;
using System.Collections.Generic;
using System.Linq;

namespace Interchange.Headers
{
    public struct AckHeader
    {
        public readonly ushort SequenceNumber;

        private AckHeader(ushort sequenceNumber) {
            this.SequenceNumber = sequenceNumber;
        }

        public static AckHeader FromSegment(ArraySegment<byte> segment) {
            ushort sequenceNumber = segment.ReadSequenceNumber(SystemHeader.Size);

            return new AckHeader(sequenceNumber);
        }
    }
}
