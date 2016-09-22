using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange
{
    public class Node : Node<object>
    {
#if TEST
        public Node(TestSettings testSettings) : base(testSettings) {
        }
#endif

        public Node() { }
    }
}
