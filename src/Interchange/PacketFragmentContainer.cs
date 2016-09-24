using System;
using System.Collections.Generic;
using System.Linq;

namespace Interchange
{
    public class PacketFragmentContainer : IDisposable
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

        public ushort InitialSequenceNumber { get; private set; }

        public ushort LastSequenceNumber {
            get { return (ushort)(InitialSequenceNumber + packetFragments.Length); }
        }

        public PacketFragmentContainer(ushort initialSequenceNumber, int totalFragmentCount) {
            this.InitialSequenceNumber = initialSequenceNumber;
            packetFragments = new Packet[totalFragmentCount];
        }

        public bool StorePacketFragment(ushort fragmentSequenceNumber, Packet packet) {
            lock (fragmentLockObject) {
                int fragmentNumber = fragmentSequenceNumber - InitialSequenceNumber;

                packetFragments[fragmentNumber] = packet;

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

        public void Dispose() {
            for (int i = 0; i < packetFragments.Length; i++) {
                if (packetFragments[i] != null) {
                    packetFragments[i].Dispose();
                }
            }
        }
    }
}
