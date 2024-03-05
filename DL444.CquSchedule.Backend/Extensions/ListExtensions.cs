using System;
using System.Collections.Generic;

namespace DL444.CquSchedule.Backend.Extensions
{
    internal static class ListExtension
    {
        public static void AddOrReplace<TKey, T>(this IList<T> list, TKey key, T item, IDictionary<TKey, T> dictionary, Func<T, T, T> conflictResolver)
        {
            if (dictionary.TryAdd(key, item))
            {
                list.Add(item);
            }
            else
            {
                T existingItem = dictionary[key];
                T newItem = conflictResolver(existingItem, item);
                list[list.IndexOf(existingItem)] = newItem;
                dictionary[key] = newItem;
            }
        }
    }
}
