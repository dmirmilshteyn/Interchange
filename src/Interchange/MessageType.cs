using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange
{
    public enum MessageType : byte
    {
        Ack = 1,
        Syn = 2,
        SynAck = 3,

        ReliableData = 128
    }
}
