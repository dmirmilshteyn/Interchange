using System;
using System.Collections.Generic;
using System.Linq;

namespace Interchange
{
    public interface IReadOnlyObjectPool<T>
    {
        int Size { get; }
    }
}
