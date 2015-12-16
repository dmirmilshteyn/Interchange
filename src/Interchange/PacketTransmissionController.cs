using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Interchange
{
    public class PacketTransmissionController<TTag>
    {
        PacketTransmissionObject<TTag>[] packetTransmissions;
        Queue<int> packetTransmissionOrder;

        int size;
        Node<TTag> node;

        public int SequenceNumber { get; private set; }

        public PacketTransmissionController(Node<TTag> node) {
            this.size = 1024;

            this.node = node;

            packetTransmissions = new PacketTransmissionObject<TTag>[size];
            packetTransmissionOrder = new Queue<int>();
        }

        public void RecordPacketTransmission(ushort sequenceNumber, Connection<TTag> connection, Packet packet) {
            int position = (ushort)(sequenceNumber - connection.InitialSequenceNumber);

            packetTransmissions[position].Acked = false;
            packetTransmissions[position].Packet = packet;
            packetTransmissions[position].Connection = connection;
            packetTransmissions[position].SequenceNumber = sequenceNumber;
            packetTransmissions[position].LastTransmissionTime = DateTime.UtcNow.ToBinary();

            packetTransmissionOrder.Enqueue(position);

            System.Diagnostics.Debug.WriteLine("Send packet #" + ((ushort)sequenceNumber).ToString());
        }

        public void RecordAck(Connection<TTag> connection, int ackNumber) {
            ushort position = (ushort)(ackNumber - connection.InitialSequenceNumber);

            packetTransmissions[position].Acked = true;

            // Only dispose the packet once it has been confirmed that the other side received it
            packetTransmissions[position].Packet.Dispose();

            System.Diagnostics.Debug.WriteLine("Got ack for " + ackNumber.ToString());
        }

        public async Task ProcessRetransmissions() {
            if (packetTransmissionOrder.Count > 0) {
                int position = packetTransmissionOrder.Peek();

                var transmissionObject = packetTransmissions[position];
                if (transmissionObject.Acked) {
                    packetTransmissionOrder.Dequeue();
                } else if (DateTime.UtcNow >= DateTime.FromBinary(transmissionObject.LastTransmissionTime).AddMilliseconds(1000)) {
                    packetTransmissionOrder.Dequeue();

                    await node.PerformSend(transmissionObject.Connection.RemoteEndPoint, transmissionObject.Packet);
                }
            }
        }
    }
}
