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
            ushort sequenceNumber = segment.ReadSequenceNumber(SystemHeader.Size);
            ushort ackNumber = segment.ReadSequenceNumber(SystemHeader.Size + 2);

            return new SynAckHeader(sequenceNumber, ackNumber);
        }
    }
}
