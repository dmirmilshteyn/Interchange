using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Interchange
{
    public class PacketTransmissionController<TTag>
    {
        readonly int WindowSize;

        PacketTransmissionObject<TTag>[] packetTransmissions;
        Queue<int> packetTransmissionOrder;

        Connection<TTag> connection;
        Node<TTag> node;

        public int SequenceNumber { get; private set; }

        private object processRetransmissionsLock = new object();

        public PacketTransmissionController(Connection<TTag> connection, Node<TTag> node) {
            this.WindowSize = 1024;

            this.connection = connection;
            this.node = node;

            packetTransmissions = new PacketTransmissionObject<TTag>[WindowSize];
            packetTransmissionOrder = new Queue<int>();
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

            packetTransmissions[position].Acked = false;
            packetTransmissions[position].Packet = packet;
            packetTransmissions[position].Connection = connection;
            packetTransmissions[position].SequenceNumber = sequenceNumber;
            packetTransmissions[position].LastTransmissionTime = DateTime.UtcNow.ToBinary();
            packetTransmissions[position].SendCount += 1;

            return position;
        }

        public void RecordAck(Connection<TTag> connection, int ackNumber) {
            ushort position = CalculatePosition((ushort)ackNumber, connection);

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

        public void ProcessRetransmissions() {
            lock (processRetransmissionsLock) {
                if (packetTransmissionOrder.Count > 0) {
                    int position = packetTransmissionOrder.Peek();

                    var transmissionObject = packetTransmissions[position];
                    if (transmissionObject.Acked) {
                        packetTransmissionOrder.Dequeue();
                    } else if (transmissionObject.SendCount > 5) {
                        connection.TriggerConnectionLost();
                    } else if (DateTime.UtcNow >= DateTime.FromBinary(transmissionObject.LastTransmissionTime).AddMilliseconds(transmissionObject.DetermineSendWaitPeriod())) {
                        packetTransmissionOrder.Dequeue();

                        node.PerformSend(transmissionObject.Connection.RemoteEndPoint, transmissionObject.Packet);

                        RecordPacketTransmission(transmissionObject.SequenceNumber, transmissionObject.Connection, transmissionObject.Packet);

                        packetTransmissionOrder.Enqueue(position);
                    }
                }
            }
        }

        private ushort CalculatePosition(ushort number, Connection<TTag> connection) {
            return (ushort)((number - connection.InitialSequenceNumber) % WindowSize);
        }
    }
}
