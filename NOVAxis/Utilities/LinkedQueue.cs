using System.Collections.Generic;
using System.Linq;

namespace NOVAxis.Utilities
{
    public class LinkedQueue<T> : LinkedList<T>
    {
        public void Enqueue(T value)
        {
            AddLast(value);
        }

        public void Enqueue(IEnumerable<T> values)
        {
            foreach (T value in values)
                AddLast(value);
        }

        public T Dequeue()
        {
            T value = this.First();
            RemoveFirst();

            return value;
        }

        public bool TryDequeue(out T value)
        {
            value = this.FirstOrDefault();
            if (value == null) return false;

            RemoveFirst();
            return true;
        }

        public T Peek()
        {
            return this.First();
        }

        public bool IsEmpty => Count == 0;
    }
}