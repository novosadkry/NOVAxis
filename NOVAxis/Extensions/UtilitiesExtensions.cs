using System;
using System.Collections.Generic;

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
    }
}
