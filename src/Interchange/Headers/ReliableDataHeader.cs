using System;
using System.Collections.Generic;
using System.Linq;

namespace Interchange.Headers
{
    public struct ReliableDataHeader : IHeader
    {
        public readonly ushort SequenceNumber;
        public readonly ushort PayloadSize;

        public static int Size {
            get {
                return
                  2   // 2 bytes for the sequence number
                  + 2 // 2 bytes for the payload size
                  ;
            }
        }

        public ReliableDataHeader(ushort sequenceNumber, ushort payloadSize) {
            this.SequenceNumber = sequenceNumber;
            this.PayloadSize = payloadSize;
        }

        public static ReliableDataHeader FromSegment(ArraySegment<byte> segment) {
            ushort sequenceNumber = segment.ReadSequenceNumber(SystemHeader.Size);
            ushort payloadSize = (ushort)BitConverter.ToInt16(segment.Array, segment.Offset + SystemHeader.Size + 2);

            return new ReliableDataHeader(sequenceNumber, payloadSize);
        }

        public void WriteTo(byte[] buffer, int offset) {
            BitUtility.Write(SequenceNumber, buffer, offset);
            BitUtility.Write(PayloadSize, buffer, offset + 2);
        }
    }
}
