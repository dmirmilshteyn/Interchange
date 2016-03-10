using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange
{
    public class BitUtility
    {
        public static void Write(ushort value, byte[] destination, int destinationOffset) {
            // Assume little-endian for now
            destination[destinationOffset++] = (byte)((value >> 0) & 0xff);
            destination[destinationOffset++] = (byte)((value >> 8) & 0xff);
        }

        public static void Write(byte[] source, byte[] destination, int destinationOffset) {
            Write(source, 0, destination, destinationOffset, source.Length);
        }

        public static void Write(byte[] source, int sourceOffset, byte[] destination, int destinationOffset, int length) {
            // TODO: Handle bit alignment cases
            Buffer.BlockCopy(source, sourceOffset, destination, destinationOffset, length);
        }
    }
}
