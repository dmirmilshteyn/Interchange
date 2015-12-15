using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Interchange.Tests
{
    public class BasicTests
    {
        [Fact]
        public async Task ConnectionTest() {
            using (var server = new TestNode()) {
                using (var client = new TestNode()) {
                    await server.ListenAsync();
                    await client.Connect();

                    var result = await server.ReadState();

                    Assert.Equal(result, TestNodeState.Connected);
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
                    await client.Connect();

                    for (int i = 0; i < payloads.Length; i++) {
                        await client.SendData(payloads[i]);
                        var result = await server.ReadMessage();

                        Assert.True(result.SequenceEqual(payloads[i]));
                    }
                }
            }
        }

    }
}
