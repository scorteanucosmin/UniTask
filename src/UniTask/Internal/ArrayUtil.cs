#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Cysharp.Threading.Tasks.Internal;

internal static class ArrayUtil
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnsureCapacity<T>(ref T[] array, int index)
    {
        if (array.Length <= index)
        {
            EnsureCore(ref array, index);
        }
    }

    // rare case, no inlining.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnsureCore<T>(ref T[] array, int index)
    {
        int newSize = array.Length * 2;
        T[]? newArray = new T[(index < newSize) ? newSize : (index * 2)];
        Array.Copy(array, 0, newArray, 0, array.Length);

        array = newArray;
    }

    /// <summary>
    /// Optimizing utility to avoid .ToArray() that creates buffer copy(cut to just size).
    /// </summary>
    public static (T[] array, int length) Materialize<T>(IEnumerable<T> source)
    {
        if (source is T[] array)
        {
            return (array, array.Length);
        }

        int defaultCount = 4;
        if (source is ICollection<T> coll)
        {
            defaultCount = coll.Count;
            T[]? buffer = new T[defaultCount];
            coll.CopyTo(buffer, 0);
            return (buffer, defaultCount);
        }
        else if (source is IReadOnlyCollection<T> rcoll)
        {
            defaultCount = rcoll.Count;
        }

        if (defaultCount == 0)
        {
            return (Array.Empty<T>(), 0);
        }

        {
            int index = 0;
            T[]? buffer = new T[defaultCount];
            foreach (var item in source)
            {
                EnsureCapacity(ref buffer, index);
                buffer[index++] = item;
            }

            return (buffer, index);
        }
    }
}