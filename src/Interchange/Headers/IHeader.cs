using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange.Headers
{
    public interface IHeader
    {
        void WriteTo(byte[] buffer, int offset);
    }
}
