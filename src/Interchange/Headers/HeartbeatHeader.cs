using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange.Headers
{
    public struct HeartbeatHeader : IHeader
    {
        public readonly ushort SequenceNumber;

        public static int Size {
            get {
                return
                  2   // 2 bytes for the sequence number
                  ;
            }
        }

        public HeartbeatHeader(ushort sequenceNumber) {
            this.SequenceNumber = sequenceNumber;
        }

        public static HeartbeatHeader FromSegment(ArraySegment<byte> segment) {
            ushort sequenceNumber = segment.ReadSequenceNumber(SystemHeader.Size);

            return new HeartbeatHeader(sequenceNumber);
        }

        public void WriteTo(byte[] buffer, int offset) {
            BitUtility.Write(SequenceNumber, buffer, offset);
        }
    }
}
