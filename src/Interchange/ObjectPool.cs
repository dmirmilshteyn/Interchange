using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace Interchange
{
    public class ObjectPool<T>
    {
        ConcurrentBag<T> objects;

        public ObjectPool(Func<T> generator, int initialCapacity) {
            objects = new ConcurrentBag<T>(BuildPoolSeed(generator, initialCapacity));
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
            }

            throw new NotImplementedException();
        }

        public void ReleaseObject(T releasedObject) {
            objects.Add(releasedObject);
        }
    }
}
