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

        public TestNode() {
            packetQueue = new Queue<Packet>();
            nodeStateQueue = new Queue<TestNodeState>();
        }

        public TestNode(TestSettings testSettings) : base(testSettings) {
            packetQueue = new Queue<Packet>();
            nodeStateQueue = new Queue<TestNodeState>();
        }

        protected override bool ProcessIncomingMessageAction(Connection<object> connection, Packet packet) {
            this.packetQueue.Enqueue(packet);

            bufferSemaphore.Release();

            return true;
        }

        protected override void ProcessConnectionAccepted(Connection<object> connection) {
            nodeStateQueue.Enqueue(TestNodeState.Connected);

            nodeStateSemaphore.Release();
        }

        protected override void ProcessConnectionDisconnected(Connection<object> connection) {
            nodeStateQueue.Enqueue(TestNodeState.Disconnected);

            nodeStateSemaphore.Release();
        }

        public async Task ListenAsync() {
            await base.ListenAsync(IPAddress.Loopback, 5000);
        }

        public async Task ConnectAsync() {
            await base.ConnectAsync(ServerEndPoint);
        }

        public void SendDataAsync(byte[] buffer) {
            base.SendDataAsync(this.RemoteConnection, buffer);
        }

        public async Task<Packet> ReadMessage() {
            await bufferSemaphore.WaitAsync();

            if (packetQueue.Count > 0) {
                return packetQueue.Dequeue();
            }

            throw new InvalidOperationException("No more messages available.");
        }

        public async Task<TestNodeState> ReadState() {
            await nodeStateSemaphore.WaitAsync();

            if (nodeStateQueue.Count > 0) {
                return nodeStateQueue.Dequeue();
            }

            return TestNodeState.None;
        }

        public bool IsStatesQueueEmpty() {
            return nodeStateQueue.Count == 0;
        }
    }
}
