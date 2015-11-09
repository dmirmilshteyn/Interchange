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
            Node node = new Node();
            node.ProcessIncomingMessageAction = HandleIncomingPacket;
            node.ProcessConnected = HandleConnected;
            await node.ListenAsync(IPAddress.Any, 5000);

            Node clientNode = new Node();
            clientNode.ProcessConnected = HandleClientConnected;
            await clientNode.Connect(new IPEndPoint(IPAddress.Loopback, 5000));

            await node.SendTo(new IPEndPoint(IPAddress.Loopback, 55056), new byte[] { 65, 66, 67, 68, 69 });

            while (true) {
                await Task.Delay(1);
            }
        }

        private void HandleClientConnected(EndPoint endPoint) {
            Console.WriteLine("Client connected: " + ((IPEndPoint)endPoint).Address.ToString() + ", " + ((IPEndPoint)endPoint).Port.ToString());
        }

        private void HandleConnected(EndPoint endPoint) {
            Console.WriteLine("Connected: " + ((IPEndPoint)endPoint).Address.ToString() + ", " + ((IPEndPoint)endPoint).Port.ToString());
        }

        private void HandleIncomingPacket(ArraySegment<byte> buffer) {
            Console.WriteLine(buffer.Count);
        }
    }
}
