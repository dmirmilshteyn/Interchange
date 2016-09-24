using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange.Headers
{
    public struct SystemHeader : IHeader
    {
        public readonly byte ChannelNumber;
        public readonly MessageType MessageType;

        public static int Size {
            get {
                return
                  1   // 1 byte for the message type
                  + 1 // 4 bits for the channel number
                  ;
            }
        }

        public SystemHeader(MessageType messageType, byte channelNumber) {
            this.MessageType = messageType;
            this.ChannelNumber = channelNumber;
        }

        public void WriteTo(byte[] buffer, int offset) {
            buffer[offset] = (byte)MessageType;
            buffer[offset + 1] = PackPayload();
        }

        private byte PackPayload() {
            return 0; // TODO: Pack channel # here and whatever other data can fit in 4 bits
        }

        private static void UnpackPayload(byte payload, out byte channelNumber) {
            channelNumber = payload; // TODO: Unpack channel # here
        }

        public static SystemHeader FromSegment(ArraySegment<byte> segment) {
            MessageType messageType = (MessageType)segment.Array[segment.Offset];

            byte channelNumber;
            UnpackPayload(segment.Array[segment.Offset + 1], out channelNumber);

            return new SystemHeader(messageType, channelNumber);
        }
    }
}
