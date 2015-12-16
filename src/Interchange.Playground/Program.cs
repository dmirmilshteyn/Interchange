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

            System.Diagnostics.Debug.WriteLine("Starting pool sizes:");
            System.Diagnostics.Debug.WriteLine("Packet: " + node.PacketPool.Size);
            System.Diagnostics.Debug.WriteLine("Socket Events: " + node.SocketEventArgsPool.Size);

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

            System.Diagnostics.Debug.WriteLine("Ending pool sizes:");
            System.Diagnostics.Debug.WriteLine("Packet: " + node.PacketPool.Size);
            System.Diagnostics.Debug.WriteLine("Socket Events: " + node.SocketEventArgsPool.Size);

            while (true) {
                await Task.Delay(1);
            }
        }

        private async Task HandleClient2Connected(Connection<object> connection, EndPoint endPoint) {
            Console.WriteLine("Client2 connected: " + ((IPEndPoint)endPoint).Address.ToString() + ", " + ((IPEndPoint)endPoint).Port.ToString());
        }

        private async Task HandleClientConnected(Connection<object> connection, EndPoint endPoint) {
            Console.WriteLine("Client connected: " + ((IPEndPoint)endPoint).Address.ToString() + ", " + ((IPEndPoint)endPoint).Port.ToString());
        }

        private async Task HandleConnected(Connection<object> connection, EndPoint endPoint) {
            Console.WriteLine("Connected: " + ((IPEndPoint)endPoint).Address.ToString() + ", " + ((IPEndPoint)endPoint).Port.ToString());
        }

        private async Task HandleIncomingPacket(Connection<object> connection, Packet packet) {
            foreach (byte data in packet.Payload) {
                Console.Write(data);
                Console.Write(", ");
            }

            Console.WriteLine();

            packet.Dispose();
        }
    }
}
