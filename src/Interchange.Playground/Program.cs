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

            IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Loopback, 5000);

            Node clientNode = new Node();
            clientNode.ProcessConnected = HandleClientConnected;
            await clientNode.ConnectAsync(serverEndPoint);

            Node clientNode2 = new Node();
            clientNode2.ProcessConnected = HandleClient2Connected;
            await clientNode2.ConnectAsync(serverEndPoint);

            await clientNode.SendDataAsync(clientNode.RemoteConnection, new byte[] { 40, 41, 42, 43, 44 });
            await clientNode.SendDataAsync(clientNode.RemoteConnection, new byte[] { 40, 41, 42, 43, 44, 45 });

            await clientNode2.SendDataAsync(clientNode2.RemoteConnection, new byte[] { 15, 16, 17, 18 });

            node.Dispose();

            while (true) {
                await Task.Delay(1);
            }
        }

        private void HandleClient2Connected(EndPoint endPoint) {
            Console.WriteLine("Client2 connected: " + ((IPEndPoint)endPoint).Address.ToString() + ", " + ((IPEndPoint)endPoint).Port.ToString());
        }

        private void HandleClientConnected(EndPoint endPoint) {
            Console.WriteLine("Client connected: " + ((IPEndPoint)endPoint).Address.ToString() + ", " + ((IPEndPoint)endPoint).Port.ToString());
        }

        private void HandleConnected(EndPoint endPoint) {
            Console.WriteLine("Connected: " + ((IPEndPoint)endPoint).Address.ToString() + ", " + ((IPEndPoint)endPoint).Port.ToString());
        }

        private void HandleIncomingPacket(Packet packet) {
            foreach (byte data in packet.Payload) {
                Console.Write(data);
                Console.Write(", ");
            }

            Console.WriteLine();

            packet.Dispose();
        }
    }
}
