using System;
using System.Collections.Generic;
using System.Linq;

namespace Interchange
{
    public struct CachedPacketInformation
    {
        public readonly Packet Packet;
        public readonly ushort SequenceNumber;
        public readonly int TotalFragmentCount;
        public readonly bool PublishMessage;

        public CachedPacketInformation(Packet packet, ushort sequenceNumber, int totalFragmentCount, bool publishMessage) {
            this.Packet = packet;
            this.SequenceNumber = sequenceNumber;
            this.TotalFragmentCount = totalFragmentCount;
            this.PublishMessage = publishMessage;
        }
    }
}
