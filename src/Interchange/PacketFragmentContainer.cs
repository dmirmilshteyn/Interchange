using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange
{
    public class PacketFragmentContainer
    {
        Packet[] packetFragments;

        object fragmentLockObject = new object();

        public int PacketLength {
            get {
                int size = 0;
                for (int i = 0; i < packetFragments.Length; i++) {
                    size += packetFragments[i].Payload.Count;
                }
                return size;
            }
        }

        public PacketFragmentContainer(ushort sequenceNumber, int totalFragmentCount) {
            packetFragments = new Packet[totalFragmentCount];
        }

        public bool StorePacketFragment(byte fragment, Packet packet) {
            lock (fragmentLockObject) {
                packetFragments[fragment] = packet;

                return IsPacketComplete();
            }
        }

        public int CopyInto(byte[] buffer) {
            int bufferOffset = 0;
            for (int i = 0; i < packetFragments.Length; i++) {
                Buffer.BlockCopy(packetFragments[i].BackingBuffer, packetFragments[i].Payload.Offset, buffer, bufferOffset, packetFragments[i].Payload.Count);

                bufferOffset += packetFragments[i].Payload.Count;
            }

            return bufferOffset;
        }

        private bool IsPacketComplete() {
            for (int i = 0; i < packetFragments.Length; i++) {
                if (packetFragments[i] == null) {
                    return false;
                }
            }

            return true;
        }
    }
}
