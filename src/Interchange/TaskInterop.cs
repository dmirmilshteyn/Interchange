using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange
{
    public class TaskInterop
    {
#if DNX451 || NET451
        public static readonly Task CompletedTask = Task.FromResult(true);
#else
        public static readonly Task CompletedTask = Task.CompletedTask;
#endif
    }
}
