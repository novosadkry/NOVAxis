using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

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

        public static Task InvokeAsync(this AsyncEventHandler eventHandler, object sender)
        {
            ArgumentNullException.ThrowIfNull(sender);

            if (eventHandler == null)
                return Task.CompletedTask;

            var tasks = eventHandler.GetInvocationList()
                .Cast<AsyncEventHandler>()
                .Select(e => e.Invoke(sender, EventArgs.Empty));

            return Task.WhenAll(tasks);
        }

        public static Task InvokeAsync<TEventArgs>(this AsyncEventHandler<TEventArgs> eventHandler, object sender, TEventArgs eventArgs)
        {
            ArgumentNullException.ThrowIfNull(sender);
            ArgumentNullException.ThrowIfNull(eventArgs);

            if (eventHandler == null)
                return Task.CompletedTask;

            var tasks = eventHandler.GetInvocationList()
                .Cast<AsyncEventHandler<TEventArgs>>()
                .Select(e => e.Invoke(sender, eventArgs));

            return Task.WhenAll(tasks);
        }
    }
}
