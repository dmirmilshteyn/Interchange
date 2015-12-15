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
            var server = new TestNode();
            var client = new TestNode();

            await server.ListenAsync();
            await client.Connect();

            var result = await server.ReadState();

            server.Close();
            client.Close();

            Assert.Equal(result, TestNodeState.Connected);
        }

        [Fact]
        public async Task SingleMessageTest() {
            var server = new TestNode();
            var client = new TestNode();

            await server.ListenAsync();
            await client.Connect();

            byte[] payload = new byte[] { 40, 41, 42, 43, 44 };
            await client.SendData(payload);

            var result = await server.ReadMessage();

            server.Close();
            client.Close();

            Assert.True(result.SequenceEqual(payload));
        }
    }
}
