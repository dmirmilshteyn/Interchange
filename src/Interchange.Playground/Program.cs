using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Interchange.Playground
{
    public class Program
    {
        public void Main(string[] args) {
            Node node = new Node();
            node.ProcessIncomingMessageAction = HandleIncomingPacket;
            node.ListenAsync(IPAddress.Any, 5000).Wait();

            while (true) {
                Task.Delay(1);
            }
        }

        private void HandleIncomingPacket(ArraySegment<byte> buffer) {
            Console.WriteLine(buffer.Count);
        }
    }
}
