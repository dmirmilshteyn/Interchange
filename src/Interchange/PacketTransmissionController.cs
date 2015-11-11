using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange
{
    public class PacketTransmissionController
    {
        PacketTransmissionObject[] packetTransmissions;

        int size;

        public int Offset { get; private set; }
        public int SequenceNumber { get; private set; }

        public PacketTransmissionController(int offset) {
            this.size = 1024;
            packetTransmissions = new PacketTransmissionObject[size];

            this.Offset = offset;
        }

        public void RecordPacketTransmission(int sequenceNumber, byte[] packet) {
            int position = (ushort)(sequenceNumber - Offset);

            packetTransmissions[position].Acked = false;
            packetTransmissions[position].Buffer = packet;
            packetTransmissions[position].SequenceNumber = sequenceNumber;
            packetTransmissions[position].LastTransmissionTime = DateTime.UtcNow.ToBinary();

            System.Diagnostics.Debug.WriteLine("Send packet #" + ((ushort)sequenceNumber).ToString());
        }

        public void RecordAck(int ackNumber) {
            ushort position = (ushort)(ackNumber - Offset);

            packetTransmissions[position].Acked = true;

            System.Diagnostics.Debug.WriteLine("Got ack for " + ackNumber.ToString());
        }

        private ushort CalculatePosition(int sequenceNumber) {
            return (ushort)(sequenceNumber - Offset);
        }
    }
}
