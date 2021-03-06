﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Interchange
{
    public sealed class Packet : IDisposable
    {
        ObjectPool<Packet> packetPool;

        bool disposed;

        public byte[] BackingBuffer { get; private set; }
        public ArraySegment<byte> Payload { get; private set; }

        public bool CandidateForDisposal { get; set; } = true;

        private Packet() {
        }

        internal Packet(ObjectPool<Packet> packetPool, byte[] backingBuffer) {
            this.packetPool = packetPool;
            this.BackingBuffer = backingBuffer;
        }

        internal void Initialize() {
            this.disposed = false;
            this.CandidateForDisposal = true;
            this.Payload = default(ArraySegment<byte>);
        }

        public void MarkPayloadRegion(int offset, int count) {
            this.Payload = new ArraySegment<byte>(this.BackingBuffer, offset, count);
        }

        public void Dispose() {
            if (!disposed) {
                disposed = true;

                // Release this packet back to the pool
                if (packetPool != null) {
                    packetPool.ReleaseObject(this);
                }
            }
        }
    }
}
