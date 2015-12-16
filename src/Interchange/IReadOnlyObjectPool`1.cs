using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange
{
    public interface IReadOnlyObjectPool<T>
    {
        int Size { get; }
    }
}
