using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange.Headers
{
    public struct SynHeader
    {
        public readonly ushort SequenceNumber;

        private SynHeader(ushort sequenceNumber) {
            this.SequenceNumber = sequenceNumber;
        }

        public static SynHeader FromSegment(ArraySegment<byte> segment) {
            ushort sequenceNumber = segment.ReadSequenceNumber(SystemHeader.Size);
            return new SynHeader(sequenceNumber);
        }
    }
}
