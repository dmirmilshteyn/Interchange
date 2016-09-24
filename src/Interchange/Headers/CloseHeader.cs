using System;
using System.Collections.Generic;
using System.Linq;

namespace Interchange.Headers
{
    public struct CloseHeader
    {
        public readonly ushort SequenceNumber;

        private CloseHeader(ushort sequenceNumber) {
            this.SequenceNumber = sequenceNumber;
        }

        public static CloseHeader FromSegment(ArraySegment<byte> segment) {
            ushort sequenceNumber = segment.ReadSequenceNumber(SystemHeader.Size);

            return new CloseHeader(sequenceNumber);
        }
    }
}
