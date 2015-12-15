using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Interchange
{
    public class PacketTransmissionController
    {
        PacketTransmissionObject[] packetTransmissions;
        Queue<int> packetTransmissionOrder;

        int size;
        Node node;

        public int SequenceNumber { get; private set; }

        public PacketTransmissionController(Node node) {
            this.size = 1024;

            this.node = node;

            packetTransmissions = new PacketTransmissionObject[size];
            packetTransmissionOrder = new Queue<int>();
        }

        public void RecordPacketTransmission(ushort sequenceNumber, Connection connection, byte[] packet) {
            int position = (ushort)(sequenceNumber - connection.InitialSequenceNumber);

            packetTransmissions[position].Acked = false;
            packetTransmissions[position].Buffer = packet;
            packetTransmissions[position].Connection = connection;
            packetTransmissions[position].SequenceNumber = sequenceNumber;
            packetTransmissions[position].LastTransmissionTime = DateTime.UtcNow.ToBinary();

            packetTransmissionOrder.Enqueue(position);

            System.Diagnostics.Debug.WriteLine("Send packet #" + ((ushort)sequenceNumber).ToString());
        }

        public void RecordAck(Connection connection, int ackNumber) {
            ushort position = (ushort)(ackNumber - connection.InitialSequenceNumber);

            packetTransmissions[position].Acked = true;

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

                    await node.SendTo(transmissionObject.Connection.RemoteEndPoint, transmissionObject.Buffer);
                }
            }
        }
    }
}
