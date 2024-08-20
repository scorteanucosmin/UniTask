#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System;
using System.Threading;

namespace Cysharp.Threading.Tasks;

public class AsyncLazy
{
    private static Action<object> continuation = SetCompletionSource;

    private Func<UniTask> taskFactory;
    private UniTaskCompletionSource completionSource;
    private UniTask.Awaiter awaiter;

    private object syncLock;
    private bool initialized;

    public AsyncLazy(Func<UniTask> taskFactory)
    {
        this.taskFactory = taskFactory;
        completionSource = new UniTaskCompletionSource();
        syncLock = new object();
        initialized = false;
    }

    internal AsyncLazy(UniTask task)
    {
        taskFactory = null;
        completionSource = new UniTaskCompletionSource();
        syncLock = null;
        initialized = true;

        UniTask.Awaiter awaiter = task.GetAwaiter();
        if (awaiter.IsCompleted)
        {
            SetCompletionSource(awaiter);
        }
        else
        {
            this.awaiter = awaiter;
            awaiter.SourceOnCompleted(continuation, this);
        }
    }

    public UniTask Task
    {
        get
        {
            EnsureInitialized();
            return completionSource.Task;
        }
    }


    public UniTask.Awaiter GetAwaiter() => Task.GetAwaiter();

    private void EnsureInitialized()
    {
        if (Volatile.Read(ref initialized))
        {
            return;
        }

        EnsureInitializedCore();
    }

    private void EnsureInitializedCore()
    {
        lock (syncLock)
        {
            if (!Volatile.Read(ref initialized))
            {
                Func<UniTask>? f = Interlocked.Exchange(ref taskFactory, null);
                if (f != null)
                {
                    UniTask task = f();
                    UniTask.Awaiter awaiter = task.GetAwaiter();
                    if (awaiter.IsCompleted)
                    {
                        SetCompletionSource(awaiter);
                    }
                    else
                    {
                        this.awaiter = awaiter;
                        awaiter.SourceOnCompleted(continuation, this);
                    }

                    Volatile.Write(ref initialized, true);
                }
            }
        }
    }

    private void SetCompletionSource(in UniTask.Awaiter awaiter)
    {
        try
        {
            awaiter.GetResult();
            completionSource.TrySetResult();
        }
        catch (Exception ex)
        {
            completionSource.TrySetException(ex);
        }
    }

    private static void SetCompletionSource(object state)
    {
        AsyncLazy? self = (AsyncLazy)state;
        try
        {
            self.awaiter.GetResult();
            self.completionSource.TrySetResult();
        }
        catch (Exception ex)
        {
            self.completionSource.TrySetException(ex);
        }
        finally
        {
            self.awaiter = default;
        }
    }
}

public class AsyncLazy<T>
{
    private static Action<object> continuation = SetCompletionSource;

    private Func<UniTask<T>> taskFactory;
    private UniTaskCompletionSource<T> completionSource;
    private UniTask<T>.Awaiter awaiter;

    private object syncLock;
    private bool initialized;

    public AsyncLazy(Func<UniTask<T>> taskFactory)
    {
        this.taskFactory = taskFactory;
        completionSource = new UniTaskCompletionSource<T>();
        syncLock = new object();
        initialized = false;
    }

    internal AsyncLazy(UniTask<T> task)
    {
        taskFactory = null;
        completionSource = new UniTaskCompletionSource<T>();
        syncLock = null;
        initialized = true;

        UniTask<T>.Awaiter awaiter = task.GetAwaiter();
        if (awaiter.IsCompleted)
        {
            SetCompletionSource(awaiter);
        }
        else
        {
            this.awaiter = awaiter;
            awaiter.SourceOnCompleted(continuation, this);
        }
    }

    public UniTask<T> Task
    {
        get
        {
            EnsureInitialized();
            return completionSource.Task;
        }
    }


    public UniTask<T>.Awaiter GetAwaiter() => Task.GetAwaiter();

    private void EnsureInitialized()
    {
        if (Volatile.Read(ref initialized))
        {
            return;
        }

        EnsureInitializedCore();
    }

    private void EnsureInitializedCore()
    {
        lock (syncLock)
        {
            if (!Volatile.Read(ref initialized))
            {
                Func<UniTask<T>>? f = Interlocked.Exchange(ref taskFactory, null);
                if (f != null)
                {
                    UniTask<T> task = f();
                    UniTask<T>.Awaiter awaiter = task.GetAwaiter();
                    if (awaiter.IsCompleted)
                    {
                        SetCompletionSource(awaiter);
                    }
                    else
                    {
                        this.awaiter = awaiter;
                        awaiter.SourceOnCompleted(continuation, this);
                    }

                    Volatile.Write(ref initialized, true);
                }
            }
        }
    }

    private void SetCompletionSource(in UniTask<T>.Awaiter awaiter)
    {
        try
        {
            var result = awaiter.GetResult();
            completionSource.TrySetResult(result);
        }
        catch (Exception ex)
        {
            completionSource.TrySetException(ex);
        }
    }

    private static void SetCompletionSource(object state)
    {
        AsyncLazy<T>? self = (AsyncLazy<T>)state;
        try
        {
            var result = self.awaiter.GetResult();
            self.completionSource.TrySetResult(result);
        }
        catch (Exception ex)
        {
            self.completionSource.TrySetException(ex);
        }
        finally
        {
            self.awaiter = default;
        }
    }
}