using Cysharp.Threading.Tasks.Internal;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Cysharp.Threading.Tasks.Linq;

public static partial class UniTaskAsyncEnumerable
{
    public static UniTask<TSource[]> ToArrayAsync<TSource>(this IUniTaskAsyncEnumerable<TSource> source, CancellationToken cancellationToken = default)
    {
        Error.ThrowArgumentNullException(source, nameof(source));

        return ToArray.ToArrayAsync(source, cancellationToken);
    }
}

internal static class ToArray
{
    internal static async UniTask<TSource[]> ToArrayAsync<TSource>(IUniTaskAsyncEnumerable<TSource> source, CancellationToken cancellationToken)
    {
        ArrayPool<TSource>? pool = ArrayPool<TSource>.Shared;
        TSource[]? array = pool.Rent(16);

        TSource[] result = default;
        IUniTaskAsyncEnumerator<TSource> e = default;
        try
        {
            e = source.GetAsyncEnumerator(cancellationToken);
            int i = 0;
            while (await e.MoveNextAsync())
            {
                ArrayPoolUtil.EnsureCapacity(ref array, i, pool);
                array[i++] = e.Current;
            }

            if (i == 0)
            {
                result = Array.Empty<TSource>();
            }
            else
            {
                result = new TSource[i];
                Array.Copy(array, result, i);
            }
        }
        finally
        {
            pool.Return(array, clearArray: !RuntimeHelpersAbstraction.IsWellKnownNoReferenceContainsType<TSource>());

            if (e != null)
            {
                await e.DisposeAsync();
            }
        }

        return result;
    }
}