using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange.Headers
{
    public struct SynAckHeader : IPacketHeader
    {
        public readonly ushort AckNumber;
        public readonly ushort SequenceNumber;

        private SynAckHeader(ushort sequenceNumber, ushort ackNumber) {
            this.SequenceNumber = sequenceNumber;
            this.AckNumber = ackNumber;
        }

        public static SynAckHeader FromSegment(ArraySegment<byte> segment) {
            ushort sequenceNumber = segment.ReadSequenceNumber(1);
            ushort ackNumber = segment.ReadSequenceNumber(3);

            return new SynAckHeader(sequenceNumber, ackNumber);
        }
    }
}
