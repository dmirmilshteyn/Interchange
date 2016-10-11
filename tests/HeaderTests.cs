using Interchange.Headers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Interchange.Tests
{
    public class HeaderTests
    {
        private byte[] GenerateHeaderBuffer<T>(T header) where T : IHeader {
            var systemHeader = new SystemHeader(MessageType.ReliableData, 0);

            var buffer = new byte[256];
            systemHeader.WriteTo(buffer, 0);
            header.WriteTo(buffer, SystemHeader.Size);

            return buffer;
        }

        [Fact]
        public void ValidateReliableDataHeader() {
            ushort sequenceNumber = 10;
            ushort payloadSize = 40;

            var header = new ReliableDataHeader(sequenceNumber, payloadSize);

            Assert.Equal(sequenceNumber, header.SequenceNumber);
            Assert.Equal(payloadSize, header.PayloadSize);

            var buffer = GenerateHeaderBuffer(header);
            var secondHeader = ReliableDataHeader.FromSegment(new ArraySegment<byte>(buffer));

            Assert.Equal(sequenceNumber, secondHeader.SequenceNumber);
            Assert.Equal(payloadSize, secondHeader.PayloadSize);
        }

        [Fact]
        public void ValidateHeartbeatHeader() {
            ushort sequenceNumber = 10;

            var header = new HeartbeatHeader(sequenceNumber);

            Assert.Equal(sequenceNumber, header.SequenceNumber);

            var buffer = GenerateHeaderBuffer(header);
            var secondHeader = HeartbeatHeader.FromSegment(new ArraySegment<byte>(buffer));

            Assert.Equal(sequenceNumber, secondHeader.SequenceNumber);
        }

        [Fact]
        public void ValidateFragmentedReliableDataHeader() {
            ushort sequenceNumber = 10;
            ushort payloadSize = 40;
            ushort totalFragmentCount = 5;

            var header = new FragmentedReliableDataHeader(sequenceNumber, payloadSize, totalFragmentCount);

            Assert.Equal(sequenceNumber, header.SequenceNumber);
            Assert.Equal(payloadSize, header.PayloadSize);
            Assert.Equal(totalFragmentCount, header.TotalFragmentCount);

            var buffer = GenerateHeaderBuffer(header);
            var secondHeader = FragmentedReliableDataHeader.FromSegment(new ArraySegment<byte>(buffer));

            Assert.Equal(sequenceNumber, secondHeader.SequenceNumber);
        }
    }
}
