using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Interchange
{
    public class PacketTransmissionController<TTag>
    {
        readonly int WindowSize;

        PacketTransmissionObject<TTag>[] packetTransmissions;
        Queue<int> packetTransmissionOrder;

        Node<TTag> node;

        public int SequenceNumber { get; private set; }

        public PacketTransmissionController(Node<TTag> node) {
            this.WindowSize = 1024;

            this.node = node;

            packetTransmissions = new PacketTransmissionObject<TTag>[WindowSize];
            packetTransmissionOrder = new Queue<int>();
        }

        public void RecordPacketTransmission(ushort sequenceNumber, Connection<TTag> connection, Packet packet) {
            packet.CandidateForDisposal = false;

            int position = CalculatePosition(sequenceNumber, connection);

            packetTransmissions[position].Acked = false;
            packetTransmissions[position].Packet = packet;
            packetTransmissions[position].Connection = connection;
            packetTransmissions[position].SequenceNumber = sequenceNumber;
            packetTransmissions[position].LastTransmissionTime = DateTime.UtcNow.ToBinary();

            packetTransmissionOrder.Enqueue(position);
        }

        public void RecordAck(Connection<TTag> connection, int ackNumber) {
            ushort position = CalculatePosition((ushort)ackNumber, connection);

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
            if (packetTransmissionOrder.Count > 0) {
                int position = packetTransmissionOrder.Peek();

                var transmissionObject = packetTransmissions[position];
                if (transmissionObject.Acked) {
                    packetTransmissionOrder.Dequeue();
                } else if (DateTime.UtcNow >= DateTime.FromBinary(transmissionObject.LastTransmissionTime).AddMilliseconds(1000)) {
                    packetTransmissionOrder.Dequeue();

                    Task.Run(() => node.PerformSend(transmissionObject.Connection.RemoteEndPoint, transmissionObject.Packet));
                }
            }
        }

        private ushort CalculatePosition(ushort number, Connection<TTag> connection) {
            return (ushort)((number - connection.InitialSequenceNumber) % WindowSize);
        }
    }
}
