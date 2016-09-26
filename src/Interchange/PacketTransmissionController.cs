using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Interchange
{
    internal class PacketTransmissionController<TTag>
    {
        readonly int WindowSize;

        PacketTransmissionObject<TTag>[] packetTransmissions;
        Queue<int> packetTransmissionOrder;

        Connection<TTag> connection;
        Node<TTag> node;

        public int SequenceNumber { get; private set; }

        private object processRetransmissionsLock = new object();
        private object packetTransmissionsLock = new object();

        DateTime lastConfirmedTransmitTime = DateTime.MinValue;

        public PacketTransmissionController(Connection<TTag> connection, Node<TTag> node) {
            this.WindowSize = 1024;

            this.connection = connection;
            this.node = node;

            packetTransmissions = new PacketTransmissionObject<TTag>[WindowSize];
            packetTransmissionOrder = new Queue<int>();
        }

        public void Initialize() {
            lastConfirmedTransmitTime = DateTime.UtcNow;
        }

        public void RecordPacketTransmissionAndEnqueue(ushort sequenceNumber, Connection<TTag> connection, Packet packet) {
            var position = RecordPacketTransmission(sequenceNumber, connection, packet);

            lock (processRetransmissionsLock) {
                packetTransmissionOrder.Enqueue(position);
            }
        }

        private int RecordPacketTransmission(ushort sequenceNumber, Connection<TTag> connection, Packet packet) {
            packet.CandidateForDisposal = false;

            int position = CalculatePosition(sequenceNumber, connection);

            lock (packetTransmissionsLock) {
                RecordPacketTransmissionUnsafe(position, sequenceNumber, connection, packet);
            }

            return position;
        }

        private void RecordPacketTransmissionUnsafe(int position, ushort sequenceNumber, Connection<TTag> connection, Packet packet) {
            packetTransmissions[position].Acked = false;
            packetTransmissions[position].Packet = packet;
            packetTransmissions[position].Connection = connection;
            packetTransmissions[position].SequenceNumber = sequenceNumber;
            packetTransmissions[position].LastTransmissionTime = DateTime.UtcNow.ToBinary();
            packetTransmissions[position].SendCount += 1;
        }

        public void RecordAck(Connection<TTag> connection, int ackNumber) {
            ushort position = CalculatePosition((ushort)ackNumber, connection);

            lastConfirmedTransmitTime = DateTime.UtcNow;

            lock (packetTransmissionsLock) {
                packetTransmissions[position].SendCount = 0;

                if (!packetTransmissions[position].Acked) {
                    packetTransmissions[position].Acked = true;

                    // Only dispose the packet once it has been confirmed that the other side received it
                    packetTransmissions[position].Packet.Dispose();

                    // Got ack
                } else {
                    // Got duplicate ack
                }
            }
        }

        public void ProcessRetransmissions() {
            lock (processRetransmissionsLock) {
                if (connection.State != ConnectionState.Disconnected) {
                    if (lastConfirmedTransmitTime != DateTime.MinValue && DateTime.UtcNow > lastConfirmedTransmitTime.AddMilliseconds(20000)) {
                        connection.TriggerConnectionLost();
                        return;
                    }

                    if (packetTransmissionOrder.Count > 0) {
                        int position = packetTransmissionOrder.Peek();

                        lock (packetTransmissionsLock) {
                            var transmissionObject = packetTransmissions[position];
                            if (transmissionObject.Acked) {
                                packetTransmissionOrder.Dequeue();
                            } else if (DateTime.UtcNow >= DateTime.FromBinary(transmissionObject.LastTransmissionTime).AddMilliseconds(transmissionObject.DetermineSendWaitPeriod())) {
                                packetTransmissionOrder.Dequeue();

                                node.PerformSend(transmissionObject.Connection.RemoteEndPoint, transmissionObject.Packet);

                                RecordPacketTransmissionUnsafe(position, transmissionObject.SequenceNumber, transmissionObject.Connection, transmissionObject.Packet);

                                packetTransmissionOrder.Enqueue(position);
                            }
                        }
                    } else if (packetTransmissionOrder.Count == 0) {
                        if (lastConfirmedTransmitTime != DateTime.MinValue && DateTime.UtcNow > lastConfirmedTransmitTime.AddMilliseconds(10000)) {
                            // Reset the timeout to allow the heartbeat to be processed
                            // Only one heartbeat will be active at a time, as this block is only entered when there are no pending packets
                            lastConfirmedTransmitTime = DateTime.UtcNow;
                            lock (connection) {
                                var packetSequenceNumber = connection.SequenceNumber;
                                connection.IncrementSequenceNumber();

                                node.SendHeartbeatPacket(connection, packetSequenceNumber);
                            }
                        }
                    }
                }
            }
        }

        private ushort CalculatePosition(ushort number, Connection<TTag> connection) {
            return (ushort)((number - connection.InitialSequenceNumber) % WindowSize);
        }
    }
}
