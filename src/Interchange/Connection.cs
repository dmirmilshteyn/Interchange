﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Interchange
{
    public class Connection
    {
        public ConnectionState State { get; internal set; }
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

        public PacketTransmissionController PacketTransmissionController { get; private set; }

        public Connection(Node node, EndPoint remoteEndPoint) {
            this.RemoteEndPoint = remoteEndPoint;

            this.sequenceNumber = InitialSequenceNumber = 0;//random.Next(ushort.MaxValue, ushort.MaxValue + 1);

            PacketTransmissionController = new PacketTransmissionController(node);
        }

        public void IncrementSequenceNumber() {
            Interlocked.Increment(ref sequenceNumber);
        }

        public void IncrementAckNumber() {
            Interlocked.Increment(ref ackNumber);
        }

        public void InitializeAckNumber(ushort ackNumber) {
            this.ackNumber = ackNumber;
        }

        public async Task Update() {
            await PacketTransmissionController.ProcessRetransmissions();
        }
    }
}
