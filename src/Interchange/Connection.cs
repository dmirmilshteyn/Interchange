using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Interchange
{
    public class Connection<TTag>
    {
        internal TaskCompletionSource<bool> DisconnectTcs { get; set; }
        internal ushort DisconnectSequenceNumber { get; set; }

        public ConnectionState State { get; internal set; }
        public EndPoint RemoteEndPoint { get; private set; }

        int sequenceNumber;
        int ackNumber;

        public int InitialSequenceNumber { get; private set; }
        public ushort SequenceNumber {
            get { return (ushort)sequenceNumber; }
        }

        public ushort AckNumber {
            get { return (ushort)ackNumber; }
        }

        public PacketTransmissionController<TTag> PacketTransmissionController { get; private set; }

        public TTag Tag { get; set; }

        Node<TTag> node;

        ConcurrentDictionary<ushort, Packet> packetCache;
        Dictionary<ushort, PacketFragmentContainer> fragmentCache;

        public Connection(Node<TTag> node, EndPoint remoteEndPoint) {
            this.RemoteEndPoint = remoteEndPoint;
            this.node = node;

            // TODO: Randomize this
            this.sequenceNumber = InitialSequenceNumber = 0;//random.Next(ushort.MaxValue, ushort.MaxValue + 1);

            PacketTransmissionController = new PacketTransmissionController<TTag>(node);
            packetCache = new ConcurrentDictionary<ushort, Packet>();
            fragmentCache = new Dictionary<ushort, PacketFragmentContainer>();
        }

        internal PacketFragmentContainer RetreivePacketFragmentContainer(ushort sequenceNumber, byte totalFragmentCount) {
            PacketFragmentContainer fragmentContainer;
            lock (fragmentCache) {
                if (!fragmentCache.TryGetValue(sequenceNumber, out fragmentContainer)) {
                    fragmentContainer = new PacketFragmentContainer(sequenceNumber, totalFragmentCount);
                    fragmentCache.Add(sequenceNumber, fragmentContainer);
                }
            }

            return fragmentContainer;
        }

        internal void RemovePacketFragmentContainer(ushort sequenceNumber) {
            lock (fragmentCache) {
                fragmentCache.Remove(sequenceNumber);
            }
        }

        internal void CachePacket(ushort sequenceNumber, Packet packet) {
            packetCache.TryAdd(sequenceNumber, packet);
        }

        internal IEnumerable<Packet> ReleaseCachedPackets(ushort currentSequenceNumber) {
            // Check if the next packet is in the cache
            currentSequenceNumber++;
            while (packetCache.Count > 0) {
                Packet packet = null;
                if (packetCache.TryRemove(currentSequenceNumber, out packet)) {
                    yield return packet;
                    // Try the next packet
                } else {
                    break;
                }
            }
        }

        public void IncrementSequenceNumber() {
            Interlocked.Increment(ref sequenceNumber);
        }

        public void IncrementAckNumber() {
            Interlocked.Increment(ref ackNumber);
        }

        public void InitializeAckNumber(ushort ackNumber) {
            this.ackNumber = ackNumber;
        }

        public void Update() {
           PacketTransmissionController.ProcessRetransmissions();
        }

        public void SendDataAsync(byte[] buffer) {
            node.SendDataAsync(this, buffer);
        }
    }

    internal struct CachedPacket
    {
        public readonly ushort SequenceNumber;
        public readonly Packet Packet;

        public CachedPacket(ushort sequenceNumber, Packet packet) {
            SequenceNumber = sequenceNumber;
            Packet = packet;
        }
    }
}
