using System;
using System.Collections.Generic;
using System.Linq;

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
