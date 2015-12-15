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

        public ushort Offset { get; private set; }
        public int SequenceNumber { get; private set; }

        public PacketTransmissionController(Node node, ushort offset) {
            this.size = 1024;

            this.node = node;

            packetTransmissions = new PacketTransmissionObject[size];
            packetTransmissionOrder = new Queue<int>();

            this.Offset = offset;
        }

        public void RecordPacketTransmission(ushort sequenceNumber, EndPoint endPoint, byte[] packet) {
            int position = (ushort)(sequenceNumber - Offset);

            packetTransmissions[position].Acked = false;
            packetTransmissions[position].Buffer = packet;
            packetTransmissions[position].EndPoint = endPoint;
            packetTransmissions[position].SequenceNumber = sequenceNumber;
            packetTransmissions[position].LastTransmissionTime = DateTime.UtcNow.ToBinary();

            packetTransmissionOrder.Enqueue(position);

            System.Diagnostics.Debug.WriteLine("Send packet #" + ((ushort)sequenceNumber).ToString());
        }

        public void RecordAck(int ackNumber) {
            ushort position = (ushort)(ackNumber - Offset);

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

                    await node.SendTo(transmissionObject.EndPoint, transmissionObject.Buffer);
                }
            }
        }

        private ushort CalculatePosition(int sequenceNumber) {
            return (ushort)(sequenceNumber - Offset);
        }
    }
}
