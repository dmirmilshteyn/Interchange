using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Interchange
{
    public class Connection<TTag> : ObservableObject
    {
        internal TaskCompletionSource<bool> DisconnectTcs { get; set; }
        internal ushort DisconnectSequenceNumber { get; set; }

        ConnectionState state;
        public ConnectionState State {
            get { return state; }
            set {
                state = value;
                RaisePropertyChanged();
            }
        }

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

        internal PacketTransmissionController<TTag> PacketTransmissionController { get; private set; }

        public TTag Tag { get; set; }

        Node<TTag> node;

        internal event EventHandler ConnectionLost;


        Dictionary<ushort, CachedPacketInformation> packetCache;
        object packetCacheLockObject = new object();

        internal object incomingDataLock = new object();

        internal PacketFragmentContainer ActiveFragmentContainer { get; set; }

        public Connection(Node<TTag> node, EndPoint remoteEndPoint) {
            this.RemoteEndPoint = remoteEndPoint;
            this.node = node;

            // TODO: Randomize this
            this.sequenceNumber = InitialSequenceNumber = 0;//random.Next(ushort.MaxValue, ushort.MaxValue + 1);

            PacketTransmissionController = new PacketTransmissionController<TTag>(this, node);
            packetCache = new Dictionary<ushort, CachedPacketInformation>();
        }

        internal bool CachePacket(ushort sequenceNumber, CachedPacketInformation packetInformation) {
            lock (packetCacheLockObject) {
                if (!packetCache.ContainsKey(sequenceNumber)) {
                    packetCache.Add(sequenceNumber, packetInformation);
                    return true;
                } else {
                    return false;
                }
            }
        }

        internal IEnumerable<CachedPacketInformation> ReleaseCachedPackets(ushort currentSequenceNumber) {
            lock (packetCacheLockObject) {
                // Check if the next packet is in the cache
                currentSequenceNumber++;
                while (packetCache.Count > 0) {
                    CachedPacketInformation packetInformation;
                    if (packetCache.TryGetValue(currentSequenceNumber, out packetInformation)) {
                        packetCache.Remove(currentSequenceNumber);
                        yield return packetInformation;
                        // Try the next packet
                        currentSequenceNumber++;
                    } else {
                        break;
                    }
                }
            }
        }

        public int IncrementSequenceNumber() {
            var currentSequenceNumber = sequenceNumber;
            Interlocked.Increment(ref sequenceNumber);
            return sequenceNumber;
        }

        public void IncrementAckNumber() {
            Interlocked.Increment(ref ackNumber);
        }

        public void InitializeAckNumber(ushort ackNumber) {
            this.ackNumber = ackNumber;

            PacketTransmissionController.Initialize();
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
