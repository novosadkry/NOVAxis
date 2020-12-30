using System.Collections.Generic;
using System.Linq;

namespace NOVAxis.Services.Audio
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

        public T Peek()
        {
            return this.First();
        }

        public bool Empty => Count == 0;
    }
}