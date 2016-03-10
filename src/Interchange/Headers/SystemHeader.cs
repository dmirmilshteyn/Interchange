using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange.Headers
{
    public struct SystemHeader
    {
        public readonly byte FragmentNumber;
        public readonly byte TotalFragmentCount;
        public readonly byte ChannelNumber;
        public readonly MessageType MessageType;

        public static int Size {
            get {
                return
                  1 // 4 bits for the fragment number, 4 bits for the channel number
                  + 1 // 4 bits for the total fragment count, 4 bits unused
                  ;
            }
        }

        public SystemHeader(MessageType messageType, byte fragmentNumber, byte totalFragmentCount, byte channelNumber) {
            this.MessageType = messageType;
            this.FragmentNumber = fragmentNumber;
            this.TotalFragmentCount = totalFragmentCount;
            this.ChannelNumber = channelNumber;
        }

        public void WriteTo(Packet packet) {
            WriteTo(packet.BackingBuffer);
        }

        public void WriteTo(byte[] buffer, int offset) {
            buffer[offset] = (byte)MessageType;
            buffer[offset + 1] = PackPayload();
            buffer[offset + 2] = TotalFragmentCount;
        }

        public void WriteTo(byte[] buffer) {
            WriteTo(buffer, 0);
        }

        private byte PackPayload() {
            return FragmentNumber; // + channel number, packed into a single byte
        }

        private static void UnpackPayload(byte payload, out byte fragmentNumber, out byte channelNumber) {
            fragmentNumber = payload; // For now, since channel number is not yet packed
            channelNumber = 0;
        }

        public static SystemHeader FromSegment(ArraySegment<byte> segment) {
            MessageType messageType = (MessageType)segment.Array[segment.Offset];

            byte fragmentNumber;
            byte channelNumber;
            UnpackPayload(segment.Array[segment.Offset + 1], out fragmentNumber, out channelNumber);
            byte totalFragmentCount = segment.Array[segment.Offset + 2];

            return new SystemHeader(messageType, fragmentNumber, totalFragmentCount, channelNumber);
        }
    }
}
