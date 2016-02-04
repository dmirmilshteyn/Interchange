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

        private Task HandleClient2Connected(Connection<object> connection, EndPoint endPoint) {
            return TaskInterop.CompletedTask;
        }

        private Task HandleClientConnected(Connection<object> connection, EndPoint endPoint) {
            return TaskInterop.CompletedTask;
        }

        private Task HandleConnected(Connection<object> connection, EndPoint endPoint) {
            return TaskInterop.CompletedTask;
        }

        private Task HandleIncomingPacket(Connection<object> connection, Packet packet) {
            return TaskInterop.CompletedTask;
        }
    }
}
