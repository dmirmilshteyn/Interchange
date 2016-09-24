using System;
using System.Collections.Generic;
using System.Linq;

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
