﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange.Headers
{
    public struct AckHeader : IPacketHeader
    {
        public readonly ushort SequenceNumber;

        private AckHeader(ushort sequenceNumber) {
            this.SequenceNumber = sequenceNumber;
        }

        public static AckHeader FromSegment(ArraySegment<byte> segment) {
            ushort sequenceNumber = segment.ReadSequenceNumber(1);

            return new AckHeader(sequenceNumber);
        }
    }
}