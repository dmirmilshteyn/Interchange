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
        Queue<Packet> packetQueue;
        Queue<TestNodeState> nodeStateQueue;

        private readonly IPEndPoint ServerEndPoint = new IPEndPoint(IPAddress.Loopback, 5000);

        SemaphoreSlim bufferSemaphore = new SemaphoreSlim(0);
        SemaphoreSlim nodeStateSemaphore = new SemaphoreSlim(0);

        SemaphoreSlim packetQueueSemaphore = new SemaphoreSlim(1, 1);
        SemaphoreSlim nodeStateQueueSemaphore = new SemaphoreSlim(1, 1);

        public TestNode() {
            packetQueue = new Queue<Packet>();
            nodeStateQueue = new Queue<TestNodeState>();
        }

        public TestNode(TestSettings testSettings) : base(testSettings) {
            packetQueue = new Queue<Packet>();
            nodeStateQueue = new Queue<TestNodeState>();
        }

        protected override bool ProcessIncomingMessageAction(Connection<object> connection, Packet packet) {
            packetQueueSemaphore.Wait();
            this.packetQueue.Enqueue(packet);
            packetQueueSemaphore.Release();

            bufferSemaphore.Release();

            return true;
        }

        protected override void ProcessConnectionAccepted(Connection<object> connection) {
            nodeStateQueueSemaphore.Wait();
            nodeStateQueue.Enqueue(TestNodeState.Connected);
            nodeStateQueueSemaphore.Release();

            nodeStateSemaphore.Release();
        }

        protected override void ProcessConnectionDisconnected(Connection<object> connection) {
            nodeStateQueueSemaphore.Wait();
            nodeStateQueue.Enqueue(TestNodeState.Disconnected);
            nodeStateQueueSemaphore.Release();

            nodeStateSemaphore.Release();
        }

        public void ListenAsync() {
            base.ListenAsync(IPAddress.Loopback, 5000);
        }

        public Task ConnectAsync() {
            return base.ConnectAsync(ServerEndPoint);
        }

        public void SendDataAsync(byte[] buffer) {
            base.SendDataAsync(this.RemoteConnection, buffer);
        }

        public async Task<Packet> ReadMessage() {
            await bufferSemaphore.WaitAsync();

            await packetQueueSemaphore.WaitAsync();
            var result = packetQueue.Dequeue();
            packetQueueSemaphore.Release();

            return result;
        }

        public async Task<TestNodeState> ReadState() {
            await nodeStateSemaphore.WaitAsync();

            await nodeStateQueueSemaphore.WaitAsync();
            var result = nodeStateQueue.Dequeue();
            nodeStateQueueSemaphore.Release();

            return result;
        }

        public bool IsStatesQueueEmpty() {
            nodeStateQueueSemaphore.Wait();
            var result = nodeStateQueue.Count == 0;
            nodeStateQueueSemaphore.Release();

            return result;
        }
    }
}
