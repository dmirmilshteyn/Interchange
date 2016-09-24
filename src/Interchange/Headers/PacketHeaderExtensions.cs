using System;
using System.Collections.Generic;
using System.Linq;

namespace Interchange.Headers
{
    public static class PacketHeaderExtensions
    {
        public static void WriteTo<T>(this T header, Packet packet) where T : IHeader { // Note: use the constrained form to prevent boxing the header struct into an IHeader reference
            header.WriteTo(packet.BackingBuffer);
        }

        public static void WriteTo<T>(this T header, byte[] buffer) where T : IHeader { // Note: use the constrained form to prevent boxing the header struct into an IHeader reference
            header.WriteTo(buffer, 0);
        }

        public static ushort ReadSequenceNumber(this ArraySegment<byte> headerBuffer, int offset) {
            return (ushort)BitConverter.ToInt16(headerBuffer.Array, headerBuffer.Offset + offset);
        }
    }
}
