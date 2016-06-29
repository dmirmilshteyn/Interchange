using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange
{
    public enum ConnectionState
    {
        None,
        HandshakeInitiated,
        Connected,
        Disconnecting,
        Disconnected
    }
}
