using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks.Internal;

namespace Cysharp.Threading.Tasks.Linq;

public static partial class UniTaskAsyncEnumerable
{
    public static IUniTaskAsyncEnumerable<T> Merge<T>(this IUniTaskAsyncEnumerable<T> first, IUniTaskAsyncEnumerable<T> second)
    {
        Error.ThrowArgumentNullException(first, nameof(first));
        Error.ThrowArgumentNullException(second, nameof(second));

        return new Merge<T>(new [] { first, second });
    }

    public static IUniTaskAsyncEnumerable<T> Merge<T>(this IUniTaskAsyncEnumerable<T> first, IUniTaskAsyncEnumerable<T> second, IUniTaskAsyncEnumerable<T> third)
    {
        Error.ThrowArgumentNullException(first, nameof(first));
        Error.ThrowArgumentNullException(second, nameof(second));
        Error.ThrowArgumentNullException(third, nameof(third));

        return new Merge<T>(new[] { first, second, third });
    }

    public static IUniTaskAsyncEnumerable<T> Merge<T>(this IEnumerable<IUniTaskAsyncEnumerable<T>> sources)
    {
        return sources is IUniTaskAsyncEnumerable<T>[] array
            ? new Merge<T>(array)
            : new Merge<T>(sources.ToArray());
    }

    public static IUniTaskAsyncEnumerable<T> Merge<T>(params IUniTaskAsyncEnumerable<T>[] sources)
    {
        return new Merge<T>(sources);
    }
}

internal sealed class Merge<T> : IUniTaskAsyncEnumerable<T>
{
    private readonly IUniTaskAsyncEnumerable<T>[] sources;

    public Merge(IUniTaskAsyncEnumerable<T>[] sources)
    {
        if (sources.Length <= 0)
        {
            Error.ThrowArgumentException("No source async enumerable to merge");
        }
        this.sources = sources;
    }

    public IUniTaskAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => new _Merge(sources, cancellationToken);

    private enum MergeSourceState
    {
        Pending,
        Running,
        Completed,
    }

    private sealed class _Merge : MoveNextSource, IUniTaskAsyncEnumerator<T>
    {
        private static readonly Action<object> GetResultAtAction = GetResultAt;

        private readonly int length;
        private readonly IUniTaskAsyncEnumerator<T>[] enumerators;
        private readonly MergeSourceState[] states;
        private readonly Queue<(T, Exception, bool)> queuedResult = new();
        private readonly CancellationToken cancellationToken;

        private int moveNextCompleted;

        public T Current { get; private set; }

        public _Merge(IUniTaskAsyncEnumerable<T>[] sources, CancellationToken cancellationToken)
        {
            this.cancellationToken = cancellationToken;
            length = sources.Length;
            states = ArrayPool<MergeSourceState>.Shared.Rent(length);
            enumerators = ArrayPool<IUniTaskAsyncEnumerator<T>>.Shared.Rent(length);
            for (int i = 0; i < length; i++)
            {
                enumerators[i] = sources[i].GetAsyncEnumerator(cancellationToken);
                states[i] = (int)MergeSourceState.Pending;;
            }
        }

        public UniTask<bool> MoveNextAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            completionSource.Reset();
            Interlocked.Exchange(ref moveNextCompleted, 0);

            if (HasQueuedResult() && Interlocked.CompareExchange(ref moveNextCompleted, 1, 0) == 0)
            {
                (T, Exception, bool) value;
                lock (states)
                {
                    value = queuedResult.Dequeue();
                }
                var resultValue = value.Item1;
                Exception? exception = value.Item2;
                bool hasNext = value.Item3;
                if (exception != null)
                {
                    completionSource.TrySetException(exception);
                }
                else
                {
                    Current = resultValue;
                    completionSource.TrySetResult(hasNext);
                }
                return new UniTask<bool>(this, completionSource.Version);
            }

            for (int i = 0; i < length; i++)
            {
                lock (states)
                {
                    if (states[i] == MergeSourceState.Pending)
                    {
                        states[i] = MergeSourceState.Running;
                    }
                    else
                    {
                        continue;
                    }
                }
                UniTask<bool>.Awaiter awaiter = enumerators[i].MoveNextAsync().GetAwaiter();
                if (awaiter.IsCompleted)
                {
                    GetResultAt(i, awaiter);
                }
                else
                {
                    awaiter.SourceOnCompleted(GetResultAtAction, StateTuple.Create(this, i, awaiter));
                }
            }
            return new UniTask<bool>(this, completionSource.Version);
        }

        public async UniTask DisposeAsync()
        {
            for (int i = 0; i < length; i++)
            {
                await enumerators[i].DisposeAsync();
            }

            ArrayPool<MergeSourceState>.Shared.Return(states, true);
            ArrayPool<IUniTaskAsyncEnumerator<T>>.Shared.Return(enumerators, true);
        }

        private static void GetResultAt(object state)
        {
            using (StateTuple<_Merge, int, UniTask<bool>.Awaiter>? tuple = (StateTuple<_Merge, int, UniTask<bool>.Awaiter>)state)
            {
                tuple.Item1.GetResultAt(tuple.Item2, tuple.Item3);
            }
        }

        private void GetResultAt(int index, UniTask<bool>.Awaiter awaiter)
        {
            bool hasNext;
            bool completedAll;
            try
            {
                hasNext = awaiter.GetResult();
            }
            catch (Exception ex)
            {
                if (Interlocked.CompareExchange(ref moveNextCompleted, 1, 0) == 0)
                {
                    completionSource.TrySetException(ex);
                }
                else
                {
                    lock (states)
                    {
                        queuedResult.Enqueue((default, ex, default));
                    }
                }
                return;
            }

            lock (states)
            {
                states[index] = hasNext ? MergeSourceState.Pending : MergeSourceState.Completed;
                completedAll = !hasNext && IsCompletedAll();
            }
            if (hasNext || completedAll)
            {
                if (Interlocked.CompareExchange(ref moveNextCompleted, 1, 0) == 0)
                {
                    Current = enumerators[index].Current;
                    completionSource.TrySetResult(!completedAll);
                }
                else
                {
                    lock (states)
                    {
                        queuedResult.Enqueue((enumerators[index].Current, null, !completedAll));
                    }
                }
            }
        }

        private bool HasQueuedResult()
        {
            lock (states)
            {
                return queuedResult.Count > 0;
            }
        }

        private bool IsCompletedAll()
        {
            lock (states)
            {
                for (int i = 0; i < length; i++)
                {
                    if (states[i] != MergeSourceState.Completed)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}