using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

namespace Interchange
{
    public class ObjectPool<T> : IReadOnlyObjectPool<T>
    {
        ConcurrentBag<T> objects;
        Func<T> generator;

        public int Size {
            get { return objects.Count; }
        }

        public ObjectPool() {
            objects = new ConcurrentBag<T>();
        }

        public ObjectPool(Func<T> generator, int initialCapacity) {
            this.generator = generator;
            objects = new ConcurrentBag<T>(BuildPoolSeed(generator, initialCapacity));
        }

        public void SeedPool(Func<T> generator, int initialCapacity) {
            this.generator = generator;
            foreach (var item in BuildPoolSeed(generator, initialCapacity)) {
                objects.Add(item);
            }
        }

        private IEnumerable<T> BuildPoolSeed(Func<T> generator, int initialCapacity) {
            for (int i = 0; i < initialCapacity; i++) {
                yield return generator();
            }
        }

        public T GetObject() {
            T result;
            if (objects.TryTake(out result)) {
                return result;
            } else {
                return generator();
            }
        }

        public void ReleaseObject(T releasedObject) {
            objects.Add(releasedObject);
        }
    }
}
