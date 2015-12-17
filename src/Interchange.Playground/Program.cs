using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Interchange.Playground
{
    public class Program
    {
        public async Task Main(string[] args) {
            while (true) {
                await Task.Delay(1);
            }
        }

        private async Task HandleClient2Connected(Connection<object> connection, EndPoint endPoint) {
        }

        private async Task HandleClientConnected(Connection<object> connection, EndPoint endPoint) {
        }

        private async Task HandleConnected(Connection<object> connection, EndPoint endPoint) {
        }

        private async Task HandleIncomingPacket(Connection<object> connection, Packet packet) {
        }
    }
}
