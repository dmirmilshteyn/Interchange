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

        internal event EventHandler ConnectionLost;

        ConcurrentDictionary<ushort, CachedPacketInformation> packetCache;
        internal PacketFragmentContainer ActiveFragmentContainer { get; set; }

        public Connection(Node<TTag> node, EndPoint remoteEndPoint) {
            this.RemoteEndPoint = remoteEndPoint;
            this.node = node;

            // TODO: Randomize this
            this.sequenceNumber = InitialSequenceNumber = 0;//random.Next(ushort.MaxValue, ushort.MaxValue + 1);

            PacketTransmissionController = new PacketTransmissionController<TTag>(this, node);
            packetCache = new ConcurrentDictionary<ushort, CachedPacketInformation>();
        }

        internal void CachePacket(ushort sequenceNumber, CachedPacketInformation packetInformation) {
            packetCache.TryAdd(sequenceNumber, packetInformation);
        }

        internal IEnumerable<CachedPacketInformation> ReleaseCachedPackets(ushort currentSequenceNumber) {
            // Check if the next packet is in the cache
            currentSequenceNumber++;
            while (packetCache.Count > 0) {
                CachedPacketInformation packetInformation;
                if (packetCache.TryRemove(currentSequenceNumber, out packetInformation)) {
                    yield return packetInformation;
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

        internal void TriggerConnectionLost() {
            this.State = ConnectionState.Disconnected;
            ConnectionLost?.Invoke(this, EventArgs.Empty);
            DisconnectTcs?.TrySetResult(true);
        }
    }
}
