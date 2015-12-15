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

        [Fact]
        public async Task SingleMessageTest() {
            using (var server = new TestNode()) {
                using (var client = new TestNode()) {
                    await server.ListenAsync();
                    await client.Connect();

                    byte[] payload = new byte[] { 40, 41, 42, 43, 44 };
                    await client.SendData(payload);

                    var result = await server.ReadMessage();

                    Assert.True(result.SequenceEqual(payload));
                }
            }
        }
    }
}
