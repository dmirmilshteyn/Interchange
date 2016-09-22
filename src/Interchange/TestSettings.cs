#if TEST

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange
{
    public class TestSettings
    {
        public Random Random { get; }

        public int SimulatedLatency { get; set; }
        public int PacketDropPercentage { get; set; }

        public TestSettings(int simulatedLatency, int packetDropPercentage) {
            this.Random = new Random();

            this.SimulatedLatency = simulatedLatency;
            this.PacketDropPercentage = packetDropPercentage;
        }
    }
}

#endif