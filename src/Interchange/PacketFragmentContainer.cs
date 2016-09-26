using System;
using System.Collections.Generic;
using System.Linq;

namespace Interchange
{
    public class PacketFragmentContainer : IDisposable
    {
        List<Packet> packetFragments;

        object fragmentLockObject = new object();

        public int PacketLength {
            get {
                int size = 0;
                for (int i = 0; i < packetFragments.Count; i++) {
                    size += packetFragments[i].Payload.Count;
                }
                return size;
            }
        }

        public ushort InitialSequenceNumber { get; private set; }
        public int TotalFragmentCount { get; }

        public PacketFragmentContainer(ushort initialSequenceNumber, int totalFragmentCount) {
            this.InitialSequenceNumber = initialSequenceNumber;
            packetFragments = new List<Packet>(totalFragmentCount);
            this.TotalFragmentCount = totalFragmentCount;
        }

        public bool AddPacketFragment(Packet packet) {
            lock (fragmentLockObject) {
                packetFragments.Add(packet);

                return IsPacketComplete();
            }
        }

        public int CopyInto(byte[] buffer) {
            int bufferOffset = 0;
            for (int i = 0; i < packetFragments.Count; i++) {
                Buffer.BlockCopy(packetFragments[i].BackingBuffer, packetFragments[i].Payload.Offset, buffer, bufferOffset, packetFragments[i].Payload.Count);

                bufferOffset += packetFragments[i].Payload.Count;
            }

            return bufferOffset;
        }

        private bool IsPacketComplete() {
            return (TotalFragmentCount == packetFragments.Count);
        }

        public void Dispose() {
            for (int i = 0; i < packetFragments.Count; i++) {
                if (packetFragments[i] != null) {
                    packetFragments[i].Dispose();
                }
            }
        }
    }
}
