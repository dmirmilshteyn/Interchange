using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Interchange.Tests
{
    public class TestNode : Node
    {
        Queue<ArraySegment<byte>> bufferQueue;
        Queue<TestNodeState> nodeStateQueue;

        private readonly IPEndPoint ServerEndPoint = new IPEndPoint(IPAddress.Loopback, 5000);

        SemaphoreSlim bufferSemaphore = new SemaphoreSlim(0);
        SemaphoreSlim nodeStateSemaphore = new SemaphoreSlim(0);

        public TestNode() {
            this.ProcessIncomingMessageAction = HandleIncomingPacket;
            this.ProcessConnected = HandleConnected;

            bufferQueue = new Queue<ArraySegment<byte>>();
            nodeStateQueue = new Queue<TestNodeState>();
        }

        public async Task ListenAsync() {
            await base.ListenAsync(IPAddress.Loopback, 5000);
        }

        public async Task Connect() {
            await base.Connect(ServerEndPoint);
        }

        public async Task SendData(byte[] buffer) {
            await base.SendData(ServerEndPoint, buffer);
        }

        private void HandleIncomingPacket(ArraySegment<byte> buffer) {
            bufferQueue.Enqueue(buffer);

            bufferSemaphore.Release();
        }

        private void HandleConnected(EndPoint endPoint) {
            nodeStateQueue.Enqueue(TestNodeState.Connected);

            nodeStateSemaphore.Release();
        }

        public async Task<ArraySegment<byte>> ReadMessage() {
            await bufferSemaphore.WaitAsync();

            if (bufferQueue.Count > 0) {
                return bufferQueue.Dequeue();
            }

            return default(ArraySegment<byte>);
        } 

        public async Task<TestNodeState> ReadState() {
            await nodeStateSemaphore.WaitAsync();

            if (nodeStateQueue.Count > 0) {
                return nodeStateQueue.Dequeue();
            }

            return TestNodeState.None;
        }
    }
}
