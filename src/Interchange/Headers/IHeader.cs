using System;
using System.Collections.Generic;
using System.Linq;

namespace Interchange.Headers
{
    public interface IHeader
    {
        void WriteTo(byte[] buffer, int offset);
    }
}
