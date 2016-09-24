﻿using System;
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
    }
}
