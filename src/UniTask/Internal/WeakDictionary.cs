#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System;
using System.Collections.Generic;
using System.Threading;

namespace Cysharp.Threading.Tasks.Internal;

// Add, Remove, Enumerate with sweep. All operations are thread safe(in spinlock).
internal class WeakDictionary<TKey, TValue>
    where TKey : class
{
    private Entry[] buckets;
    private int size;
    private SpinLock gate; // mutable struct(not readonly)

    private readonly float loadFactor;
    private readonly IEqualityComparer<TKey> keyEqualityComparer;

    public WeakDictionary(int capacity = 4, float loadFactor = 0.75f, IEqualityComparer<TKey> keyComparer = null)
    {
        int tableSize = CalculateCapacity(capacity, loadFactor);
        buckets = new Entry[tableSize];
        this.loadFactor = loadFactor;
        gate = new SpinLock(false);
        keyEqualityComparer = keyComparer ?? EqualityComparer<TKey>.Default;
    }

    public bool TryAdd(TKey key, TValue value)
    {
        bool lockTaken = false;
        try
        {
            gate.Enter(ref lockTaken);
            return TryAddInternal(key, value);
        }
        finally
        {
            if (lockTaken) gate.Exit(false);
        }
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        bool lockTaken = false;
        try
        {
            gate.Enter(ref lockTaken);
            if (TryGetEntry(key, out _, out Entry? entry))
            {
                value = entry.Value;
                return true;
            }

            value = default(TValue);
            return false;
        }
        finally
        {
            if (lockTaken) gate.Exit(false);
        }
    }

    public bool TryRemove(TKey key)
    {
        bool lockTaken = false;
        try
        {
            gate.Enter(ref lockTaken);
            if (TryGetEntry(key, out int hashIndex, out Entry? entry))
            {
                Remove(hashIndex, entry);
                return true;
            }

            return false;
        }
        finally
        {
            if (lockTaken) gate.Exit(false);
        }
    }

    private bool TryAddInternal(TKey key, TValue value)
    {
        int nextCapacity = CalculateCapacity(size + 1, loadFactor);

        TRY_ADD_AGAIN:
        if (buckets.Length < nextCapacity)
        {
            // rehash
            Entry[]? nextBucket = new Entry[nextCapacity];
            for (int i = 0; i < buckets.Length; i++)
            {
                Entry? e = buckets[i];
                while (e != null)
                {
                    AddToBuckets(nextBucket, key, e.Value, e.Hash);
                    e = e.Next;
                }
            }

            buckets = nextBucket;
            goto TRY_ADD_AGAIN;
        }
        else
        {
            // add entry
            bool successAdd = AddToBuckets(buckets, key, value, keyEqualityComparer.GetHashCode(key));
            if (successAdd) size++;
            return successAdd;
        }
    }

    private bool AddToBuckets(Entry[] targetBuckets, TKey newKey, TValue value, int keyHash)
    {
        int h = keyHash;
        int hashIndex = h & (targetBuckets.Length - 1);

        TRY_ADD_AGAIN:
        if (targetBuckets[hashIndex] == null)
        {
            targetBuckets[hashIndex] = new Entry
            {
                Key = new WeakReference<TKey>(newKey, false),
                Value = value,
                Hash = h
            };

            return true;
        }
        else
        {
            // add to last.
            Entry? entry = targetBuckets[hashIndex];
            while (entry != null)
            {
                if (entry.Key.TryGetTarget(out TKey? target))
                {
                    if (keyEqualityComparer.Equals(newKey, target))
                    {
                        return false; // duplicate
                    }
                }
                else
                {
                    Remove(hashIndex, entry);
                    if (targetBuckets[hashIndex] == null) goto TRY_ADD_AGAIN; // add new entry
                }

                if (entry.Next != null)
                {
                    entry = entry.Next;
                }
                else
                {
                    // found last
                    entry.Next = new Entry
                    {
                        Key = new WeakReference<TKey>(newKey, false),
                        Value = value,
                        Hash = h
                    };
                    entry.Next.Prev = entry;
                }
            }

            return false;
        }
    }

    private bool TryGetEntry(TKey key, out int hashIndex, out Entry entry)
    {
        Entry[]? table = buckets;
        int hash = keyEqualityComparer.GetHashCode(key);
        hashIndex = hash & table.Length - 1;
        entry = table[hashIndex];

        while (entry != null)
        {
            if (entry.Key.TryGetTarget(out TKey? target))
            {
                if (keyEqualityComparer.Equals(key, target))
                {
                    return true;
                }
            }
            else
            {
                // sweap
                Remove(hashIndex, entry);
            }

            entry = entry.Next;
        }

        return false;
    }

    private void Remove(int hashIndex, Entry entry)
    {
        if (entry.Prev == null && entry.Next == null)
        {
            buckets[hashIndex] = null;
        }
        else
        {
            if (entry.Prev == null)
            {
                buckets[hashIndex] = entry.Next;
            }
            if (entry.Prev != null)
            {
                entry.Prev.Next = entry.Next;
            }
            if (entry.Next != null)
            {
                entry.Next.Prev = entry.Prev;
            }
        }
        size--;
    }

    public List<KeyValuePair<TKey, TValue>> ToList()
    {
        List<KeyValuePair<TKey, TValue>>? list = new(size);
        ToList(ref list, false);
        return list;
    }

    // avoid allocate everytime.
    public int ToList(ref List<KeyValuePair<TKey, TValue>> list, bool clear = true)
    {
        if (clear)
        {
            list.Clear();
        }

        int listIndex = 0;

        bool lockTaken = false;
        try
        {
            for (int i = 0; i < buckets.Length; i++)
            {
                Entry? entry = buckets[i];
                while (entry != null)
                {
                    if (entry.Key.TryGetTarget(out TKey? target))
                    {
                        KeyValuePair<TKey, TValue> item = new(target, entry.Value);
                        if (listIndex < list.Count)
                        {
                            list[listIndex++] = item;
                        }
                        else
                        {
                            list.Add(item);
                            listIndex++;
                        }
                    }
                    else
                    {
                        // sweap
                        Remove(i, entry);
                    }

                    entry = entry.Next;
                }
            }
        }
        finally
        {
            if (lockTaken) gate.Exit(false);
        }

        return listIndex;
    }

    private static int CalculateCapacity(int collectionSize, float loadFactor)
    {
        int size = (int)(((float)collectionSize) / loadFactor);

        size--;
        size |= size >> 1;
        size |= size >> 2;
        size |= size >> 4;
        size |= size >> 8;
        size |= size >> 16;
        size += 1;

        if (size < 8)
        {
            size = 8;
        }
        return size;
    }

    private class Entry
    {
        public WeakReference<TKey> Key;
        public TValue Value;
        public int Hash;
        public Entry Prev;
        public Entry Next;

        // debug only
        public override string ToString()
        {
            if (Key.TryGetTarget(out TKey? target))
            {
                return target + "(" + Count() + ")";
            }
            else
            {
                return "(Dead)";
            }
        }

        private int Count()
        {
            int count = 1;
            Entry? n = this;
            while (n.Next != null)
            {
                count++;
                n = n.Next;
            }
            return count;
        }
    }
}