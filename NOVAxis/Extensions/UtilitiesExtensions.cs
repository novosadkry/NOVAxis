using System;
using System.Collections.Generic;
using System.Linq;
using NOVAxis.Utilities;

namespace NOVAxis.Extensions
{
    public static class UtilitiesExtensions
    {
        public static LinkedListNode<T> RemoveAt<T>(this LinkedList<T> list, int index)
        {
            LinkedListNode<T> currentNode = list.First;
            for (int i = 0; i <= index && currentNode != null; i++)
            {
                if (i == index)
                {
                    list.Remove(currentNode);
                    return currentNode;
                }

                currentNode = currentNode.Next;
            }

            throw new IndexOutOfRangeException();
        }

        public static int RemoveAll<T>(this LinkedList<T> list, Predicate<T> match)
        {
            var matches = list
                .Where(x => match(x))
                .ToList();

            foreach (T item in matches)
                list.Remove(item);

            return matches.Count;
        }

        public static TValue Get<TKey, TValue>(this Cache<TKey, object> cache, TKey key)
        {
            return (TValue)(cache.Get(key) ?? default(TValue));
        }
    }
}
