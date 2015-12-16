using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange
{
    public class ConnectionInitializedEventArgs<TTag> : EventArgs
    {
        public Connection<TTag> Connection { get; private set; }

        public ConnectionInitializedEventArgs(Connection<TTag> connection) {
            this.Connection = connection;
        }
    }
}
