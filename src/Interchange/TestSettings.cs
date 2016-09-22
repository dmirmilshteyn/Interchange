#if TEST

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange
{
    public class TestSettings
    {
        int packetDropValueCount = 0;

        public int SimulatedLatency { get; set; }

        public bool PacketDroppingEnabled { get; set; } = false;
        public int PacketDropPercentage { get; set; }

        public TestSettings(int simulatedLatency, int packetDropPercentage) {
            this.SimulatedLatency = simulatedLatency;
            this.PacketDropPercentage = packetDropPercentage;
        }

        public int GetNextPacketDropValue() {
            packetDropValueCount++;
            switch (packetDropValueCount % 6) {
                case 0:
                    return 0;
                case 1:
                    return 20;
                case 2:
                    return 40;
                case 3:
                    return 60;
                case 4:
                    return 80;
                case 5:
                    return 100;
            }

            return 0;
        }
    }
}

#endif