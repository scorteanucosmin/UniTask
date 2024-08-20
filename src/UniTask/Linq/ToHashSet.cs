using Cysharp.Threading.Tasks.Internal;
using System.Collections.Generic;
using System.Threading;

namespace Cysharp.Threading.Tasks.Linq;

public static partial class UniTaskAsyncEnumerable
{
    public static UniTask<HashSet<TSource>> ToHashSetAsync<TSource>(this IUniTaskAsyncEnumerable<TSource> source, CancellationToken cancellationToken = default)
    {
        Error.ThrowArgumentNullException(source, nameof(source));

        return ToHashSet.ToHashSetAsync(source, EqualityComparer<TSource>.Default, cancellationToken);
    }

    public static UniTask<HashSet<TSource>> ToHashSetAsync<TSource>(this IUniTaskAsyncEnumerable<TSource> source, IEqualityComparer<TSource> comparer, CancellationToken cancellationToken = default)
    {
        Error.ThrowArgumentNullException(source, nameof(source));
        Error.ThrowArgumentNullException(comparer, nameof(comparer));

        return ToHashSet.ToHashSetAsync(source, comparer, cancellationToken);
    }
}

internal static class ToHashSet
{
    internal static async UniTask<HashSet<TSource>> ToHashSetAsync<TSource>(IUniTaskAsyncEnumerable<TSource> source, IEqualityComparer<TSource> comparer, CancellationToken cancellationToken)
    {
        HashSet<TSource>? set = new(comparer);

        IUniTaskAsyncEnumerator<TSource>? e = source.GetAsyncEnumerator(cancellationToken);
        try
        {
            while (await e.MoveNextAsync())
            {
                set.Add(e.Current);
            }
        }
        finally
        {
            if (e != null)
            {
                await e.DisposeAsync();
            }
        }

        return set;
    }
}