using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Interchange.Tests
{
    public class BasicTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public async Task ConnectionTest(int clientCount) {
            using (var server = new TestNode()) {
                await server.ListenAsync();

                List<TestNode> clients = new List<TestNode>(clientCount);
                for (int i = 0; i < clientCount; i++) {
                    var client = new TestNode();

                    await client.ConnectAsync();

                    clients.Add(client);

                    var result = await server.ReadState();
                    Assert.Equal(result, TestNodeState.Connected);
                }

                foreach (var client in clients) {
                    client.Dispose();
                }
            }
        }

        public static IEnumerable<object[]> MessageTestPayloads {
            get {
                yield return new object[] {
                    new byte[][]
                    {
                        new byte[] { 40, 41, 42, 43, 44 },
                    }
                };

                yield return new object[] {
                    new byte[][]
                    {
                        new byte[] { 40, 41, 42, 43, 44 },
                        new byte[] { 40, 41, 42, 43, 44, 45 }
                    }
                };

                yield return new object[] {
                    new byte[][]
                    {
                        new byte[] { 40, 41, 42, 43, 44 },
                        new byte[] { 40, 41, 42, 43, 44, 45 },
                        new byte[] { 40, 41, 42, 43, 44, 45, 46, 47, 48 }
                    }
                };
            }
        }

        [Theory]
        [MemberData(nameof(MessageTestPayloads))]
        public async Task SimpleMessageTest(byte[][] payloads) {
            using (var server = new TestNode()) {
                using (var client = new TestNode()) {
                    await server.ListenAsync();
                    await client.ConnectAsync();

                    for (int i = 0; i < payloads.Length; i++) {
                        await client.SendDataAsync(payloads[i]);
                        using (var result = await server.ReadMessage()) {

                            Assert.True(result.Payload.SequenceEqual(payloads[i]));

                        }
                    }
                }
            }
        }

    }
}
