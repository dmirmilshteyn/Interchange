using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Interchange.Tests
{
    public class IntegrationTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public async Task ConnectionTest(int clientCount) {
            using (var server = new TestNode()) {
                server.ListenAsync();

                List<TestNode> clients = new List<TestNode>(clientCount);
                for (int i = 0; i < clientCount; i++) {
                    var client = new TestNode();

                    await client.ConnectAsync();

                    clients.Add(client);

                    var result = await server.ReadState();
                    Assert.Equal(TestNodeState.Connected, result);
                    Assert.True(server.IsStatesQueueEmpty());
                }

                foreach (var client in clients) {
                    client.Dispose();
                }
            }
        }

        public static IEnumerable<object[]> MessageTestPayloads() {
            return MessageTestPayloads(new int[] { 0 }, new int[] { 0 });
        }

        public static IEnumerable<object[]> MessageTestPayloads(int[] latencies, int[] dropPercentages) {
            foreach (var latency in latencies) {
                foreach (var dropPercentage in dropPercentages) {
                    yield return new object[] {
                        new byte[][]
                        {
                            new byte[] { 40, 41, 42, 43, 44 },
                        },
                        latency,
                        dropPercentage
                    };

                    yield return new object[] {
                        new byte[][]
                        {
                            new byte[] { 40, 41, 42, 43, 44 },
                            new byte[] { 40, 41, 42, 43, 44, 45 }
                        },
                        latency,
                        dropPercentage
                    };

                    yield return new object[] {
                        new byte[][]
                        {
                            new byte[] { 40, 41, 42, 43, 44 },
                            new byte[] { 40, 41, 42, 43, 44, 45 },
                            new byte[] { 40, 41, 42, 43, 44, 45, 46, 47, 48 }
                        },
                        latency,
                        dropPercentage
                    };
                }
            }
        }

        [Theory]
        [MemberData(nameof(MessageTestPayloads), new int[] { 0, 100, 300, 1000 }, new int[] { 0, 20, 40, 60, 80 })]
        public async Task SimpleMessageTest(byte[][] payloads, int latency, int dropPercentage) {
            using (var server = new TestNode()) {
                using (var client = new TestNode(new SimulationSettings(latency, dropPercentage))) {
                    server.ListenAsync();
                    await client.ConnectAsync();

                    client.SimulationSettings.PacketDroppingEnabled = true;

                    await SendPayloads(server, client, payloads);

                    await client.DisconnectAsync();
                }
            }
        }

        [Theory]
        [MemberData(nameof(MessageTestPayloads))]
        public async Task ManySimpleMessageTest(byte[][] payloads, int latency, int dropPercentage) {
            using (var server = new TestNode()) {
                using (var client = new TestNode(new SimulationSettings(latency, dropPercentage))) {
                    server.ListenAsync();
                    await client.ConnectAsync();

                    for (int i = 0; i < 1000; i++) {
                        await SendPayloads(server, client, payloads);
                    }

                    await client.DisconnectAsync();
                }
            }
        }

        // TODO: Figure out why this is failing on CI
        //[Fact]
        //public async Task ManySimpleMessageTestWithoutWaitingOrdered() {
        //    using (var server = new TestNode()) {
        //        using (var client = new TestNode()) {
        //            await server.ListenAsync();
        //            await client.ConnectAsync();

        //            for (int n = 0; n < 10000; n++) {
        //                await client.SendDataAsync(BitConverter.GetBytes(n));
        //            }

        //            for (int n = 0; n < 10000; n++) {
        //                using (var result = await server.ReadMessage()) {
        //                    Assert.True(result.Payload.SequenceEqual(BitConverter.GetBytes(n)));
        //                }
        //            }
        //        }
        //    }
        //}

        [Theory]
        [MemberData(nameof(MessageTestPayloads))]
        public async Task ManySimpleMessageTestWithoutWaiting(byte[][] payloads, int latency, int dropPercentage) {
            using (var server = new TestNode()) {
                using (var client = new TestNode(new SimulationSettings(latency, dropPercentage))) {
                    server.ListenAsync();
                    await client.ConnectAsync();

                    for (int n = 0; n < 50; n++) {
                        for (int i = 0; i < payloads.Length; i++) {
                            client.SendDataAsync(payloads[i]);
                        }
                    }

                    for (int n = 0; n < 50; n++) {
                        for (int i = 0; i < payloads.Length; i++) {
                            using (var result = await server.ReadMessage()) {
                                Assert.True(result.Payload.SequenceEqual(payloads[i]));
                            }
                        }
                    }

                    await client.DisconnectAsync();
                }
            }
        }

        private async Task SendPayloads(TestNode server, TestNode client, byte[][] payloads) {
            for (int i = 0; i < payloads.Length; i++) {
                client.SendDataAsync(payloads[i]);
                using (var result = await server.ReadMessage()) {

                    Assert.True(result.Payload.SequenceEqual(payloads[i]));
                }
            }
        }

        private void SendPayloadsWithoutWaiting(TestNode client, byte[][] payloads) {
            for (int i = 0; i < payloads.Length; i++) {
                client.SendDataAsync(payloads[i]);
            }
        }

        private async Task VerifySentPayloads(TestNode server, byte[][] payloads) {
            for (int i = 0; i < payloads.Length; i++) {
                using (var result = await server.ReadMessage()) {
                    Assert.True(result.Payload.SequenceEqual(payloads[i]));
                }
            }
        }

        private byte[] GenerateRandomBytes(int length) {
            var buffer = new byte[length];
            var rand = new Random();
            rand.NextBytes(buffer);

            return buffer;
        }

        [Fact]
        public async Task LargeMessageTest() {
            using (var server = new TestNode()) {
                using (var client = new TestNode()) {
                    server.ListenAsync();
                    await client.ConnectAsync();

                    byte[] buffer = new byte[10000];
                    Random rand = new Random();
                    rand.NextBytes(buffer);

                    await SendPayloads(server, client, new byte[][] { buffer });

                    await client.DisconnectAsync();
                }
            }
        }

        [Fact]
        public async Task MultipleLargeMessageTests() {
            using (var server = new TestNode()) {
                using (var client = new TestNode()) {
                    server.ListenAsync();
                    await client.ConnectAsync();

                    for (int i = 0; i < 10; i++) {
                        byte[] buffer = new byte[10000];
                        Random rand = new Random();
                        rand.NextBytes(buffer);

                        await SendPayloads(server, client, new byte[][] { buffer });
                    }

                    await client.DisconnectAsync();
                }
            }
        }

        [Theory]
        [MemberData(nameof(MessageTestPayloads))]
        public async Task PoolSizeTest(byte[][] payloads, int latency, int dropPercentage) {
            using (var server = new TestNode()) {
                using (var client = new TestNode(new SimulationSettings(latency, dropPercentage))) {
                    int startingServerPacketPoolSize = server.PacketPool.Size;
                    int startingClientPacketPoolSize = client.PacketPool.Size;
                    int startingServerSocketPoolSize = server.SocketEventArgsPool.Size;
                    int startingClientSocketPoolSize = client.SocketEventArgsPool.Size;

                    server.ListenAsync();
                    await client.ConnectAsync();

                    await SendPayloads(server, client, payloads);

                    // Let all the pool objects be released
                    await Task.Delay(1);

                    // There should be one less for both server and client because they are still listening/connected
                    Assert.Equal(startingServerPacketPoolSize - 1, server.PacketPool.Size);
                    Assert.Equal(startingClientPacketPoolSize - 1, client.PacketPool.Size);
                    Assert.Equal(startingServerSocketPoolSize - 1, server.SocketEventArgsPool.Size);
                    Assert.Equal(startingClientSocketPoolSize - 1, client.SocketEventArgsPool.Size);

                    await client.DisconnectAsync();
                }
            }
        }

        [Fact]
        public async Task ConnectDisconnectTest() {
            using (var server = new TestNode()) {
                using (var client = new TestNode()) {
                    server.ListenAsync();
                    await client.ConnectAsync();

                    // Ensure both the client and server are connected
                    var result = await client.ReadState();
                    Assert.Equal(TestNodeState.Connected, result);
                    Assert.True(client.IsStatesQueueEmpty());

                    result = await server.ReadState();
                    Assert.Equal(TestNodeState.Connected, result);
                    Assert.True(server.IsStatesQueueEmpty());

                    await client.DisconnectAsync();

                    // Ensure both the client and server and disconnected
                    result = await client.ReadState();
                    Assert.Equal(TestNodeState.Disconnected, result);
                    Assert.True(client.IsStatesQueueEmpty());

                    result = await server.ReadState();
                    Assert.Equal(TestNodeState.Disconnected, result);
                    Assert.True(server.IsStatesQueueEmpty());
                }
            }
        }

        [Fact]
        public async Task TestDroppedConnectionWithIncomingData() {
            using (var server = new TestNode()) {
                server.ListenAsync();

                var client = new TestNode();
                await client.ConnectAsync();

                var result = await server.ReadState();
                Assert.Equal(TestNodeState.Connected, result);
                Assert.True(server.IsStatesQueueEmpty());

                // Dispose, not a clean disconnect
                client.Dispose();

                var payload = GenerateRandomBytes(100);
                server.SendDataAsync(payload);

                result = await server.ReadState();
                Assert.Equal(TestNodeState.Disconnected, result);
                Assert.True(server.IsStatesQueueEmpty());
            }
        }

        [Fact]
        public async Task TestDroppedConnectionWithHeartbeatAndNoIncomingData() {
            using (var server = new TestNode()) {
                server.ListenAsync();

                var client = new TestNode();
                await client.ConnectAsync();

                var result = await server.ReadState();
                Assert.Equal(TestNodeState.Connected, result);
                Assert.True(server.IsStatesQueueEmpty());

                // Dispose, not a clean disconnect
                client.Dispose();

                result = await server.ReadState();
                Assert.Equal(TestNodeState.Disconnected, result);
                Assert.True(server.IsStatesQueueEmpty());
            }
        }

        [Fact]
        public async Task TestIdleConnectionKeptAliveByHearbeat() {
            using (var server = new TestNode()) {
                server.ListenAsync();

                using (var client = new TestNode()) {
                    await client.ConnectAsync();

                    var result = await server.ReadState();
                    Assert.Equal(TestNodeState.Connected, result);
                    Assert.True(server.IsStatesQueueEmpty());

                    // Wait 60 seconds doing nothing
                    await Task.Delay(60000);

                    Assert.Equal(ConnectionState.Connected, client.RemoteConnection.State);
                    Assert.Equal(ConnectionState.Connected, server.RemoteConnection.State);
                }
            }
        }

        public static IEnumerable<object[]> SequenceOrders {
            get {
                yield return new object[] {
                    new ushort[10] { 3, 5, 1, 0, 6, 2, 4, 9, 7, 8 }
                };
                yield return new object[] {
                    new ushort[10] { 9, 7, 4, 2, 1, 3, 6, 5, 8, 0 }
                };
            }
        }

        [Theory]
        [MemberData(nameof(SequenceOrders))]
        public async Task TestOutOfOrderPackets(ushort[] sequenceNumbers) {
            using (var server = new TestNode()) {
                server.ListenAsync();

                using (var client = new TestNode()) {
                    await client.ConnectAsync();

                    var payload = GenerateRandomBytes(200);

                    var startingSequenceNumber = client.RemoteConnection.SequenceNumber;

                    for (int i = 0; i < sequenceNumbers.Length; i++) {
                        var packet = client.BuildReliableDataPacket(payload, 0, payload.Length, (ushort)(startingSequenceNumber + sequenceNumbers[i]));

                        var sendSequenceNumber = (ushort)client.RemoteConnection.IncrementSequenceNumber();
                        client.SendToSequenced(client.RemoteConnection, sendSequenceNumber, packet, true);
                    }

                    for (int i = 0; i < sequenceNumbers.Length; i++) {
                        using (var result = await server.ReadMessage()) {
                            Assert.True(result.Payload.SequenceEqual(payload));
                        }
                    }
                }
            }
        }

        [Fact]
        public async Task TestHandlingDuplicatePackets() {
            using (var server = new TestNode()) {
                server.ListenAsync();

                using (var client = new TestNode()) {
                    await client.ConnectAsync();

                    var payload = GenerateRandomBytes(200);

                    var startingSequenceNumber = client.RemoteConnection.SequenceNumber;

                    for (int i = 0; i < 10; i++) {
                        var packet = client.BuildReliableDataPacket(payload, 0, payload.Length, (ushort)(startingSequenceNumber + i));

                        var sendSequenceNumber = (ushort)client.RemoteConnection.IncrementSequenceNumber();

                        // Send duplicates
                        client.SendToSequenced(client.RemoteConnection, sendSequenceNumber, packet, true);
                        client.SendToSequenced(client.RemoteConnection, sendSequenceNumber, packet, true);
                        client.SendToSequenced(client.RemoteConnection, sendSequenceNumber, packet, true);
                    }

                    for (int i = 0; i < 10; i++) {
                        using (var result = await server.ReadMessage()) {
                            Assert.True(result.Payload.SequenceEqual(payload));
                        }
                    }
                }
            }
        }
        

        [Theory(Skip = "Congestion/flow control not yet implemented")]
        [MemberData(nameof(MessageTestPayloads))]
        public async Task SendSpamTest(byte[][] payloads, int latency, int dropPercentage) {
            using (var server = new TestNode()) {
                using (var client = new TestNode(new SimulationSettings(latency, dropPercentage))) {
                    server.ListenAsync();
                    await client.ConnectAsync();

                    for (int i = 0; i < 100000; i++) {
                        SendPayloadsWithoutWaiting(client, payloads);
                    }

                    for (int i = 0; i < 100000; i++) {
                        await VerifySentPayloads(server, payloads);
                    }

                    await client.DisconnectAsync();
                }
            }
        }
    }
}
