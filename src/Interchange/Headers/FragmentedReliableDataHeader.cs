using System;
using System.Collections.Generic;
using System.Linq;

namespace Interchange.Headers
{
    public struct FragmentedReliableDataHeader : IHeader
    {
        public readonly ushort SequenceNumber;
        public readonly ushort PayloadSize;
        public readonly ushort TotalFragmentCount;

        public static int Size {
            get {
                return
                  2   // 2 bytes for the sequence number
                  + 2 // 2 bytes for the payload size
                  + 2 // 2 bytes for the total fragment count
                  ;
            }
        }

        public FragmentedReliableDataHeader(ushort sequenceNumber, ushort payloadSize, ushort totalFragmentCount) {
            this.SequenceNumber = sequenceNumber;
            this.PayloadSize = payloadSize;
            this.TotalFragmentCount = totalFragmentCount;
        }

        public static FragmentedReliableDataHeader FromSegment(ArraySegment<byte> segment) {
            ushort sequenceNumber = segment.ReadSequenceNumber(SystemHeader.Size);
            ushort payloadSize = (ushort)BitConverter.ToInt16(segment.Array, segment.Offset + SystemHeader.Size + 2);
            ushort totalFragmentCount = (ushort)BitConverter.ToInt16(segment.Array, segment.Offset + SystemHeader.Size + 2 + 2);

            return new FragmentedReliableDataHeader(sequenceNumber, payloadSize, totalFragmentCount);
        }

        public void WriteTo(byte[] buffer, int offset) {
            BitUtility.Write(SequenceNumber, buffer, offset);
            BitUtility.Write((ushort)PayloadSize, buffer, offset + 2);
            BitUtility.Write(TotalFragmentCount, buffer, offset + 2 + 2);
        }
    }
}
