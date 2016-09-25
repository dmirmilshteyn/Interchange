using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Interchange
{
    public struct PacketTransmissionObject<TTag>
    {
        public bool Acked;
        public ushort SequenceNumber;
        public Connection<TTag> Connection;
        public Packet Packet;
        public long LastTransmissionTime;
        public int SendCount;

        public int DetermineSendWaitPeriod() {
            switch (SendCount) {
                case 0:
                    return 1000;
                case 1:
                    return 1500;
                case 2:
                    return 2000;
                case 3:
                    return 2500;
                case 4:
                    return 3000;
                case 5:
                    return 3500;
                default:
                    throw new InvalidOperationException($"Invalid SendCount of {SendCount}");
            }
        }
    }
}
