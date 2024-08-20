#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks.Internal;
using Object = UnityEngine.Object;

namespace Cysharp.Threading.Tasks;

public partial struct UniTask
{
    public static UniTask WaitUntil(Func<bool> predicate, PlayerLoopTiming timing = PlayerLoopTiming.Update, CancellationToken cancellationToken = default(CancellationToken), bool cancelImmediately = false)
    {
        return new UniTask(WaitUntilPromise.Create(predicate, timing, cancellationToken, cancelImmediately, out short token), token);
    }

    public static UniTask WaitWhile(Func<bool> predicate, PlayerLoopTiming timing = PlayerLoopTiming.Update, CancellationToken cancellationToken = default(CancellationToken), bool cancelImmediately = false)
    {
        return new UniTask(WaitWhilePromise.Create(predicate, timing, cancellationToken, cancelImmediately, out short token), token);
    }

    public static UniTask WaitUntilCanceled(CancellationToken cancellationToken, PlayerLoopTiming timing = PlayerLoopTiming.Update, bool completeImmediately = false)
    {
        return new UniTask(WaitUntilCanceledPromise.Create(cancellationToken, timing, completeImmediately, out short token), token);
    }

    public static UniTask<U> WaitUntilValueChanged<T, U>(T target, Func<T, U> monitorFunction, PlayerLoopTiming monitorTiming = PlayerLoopTiming.Update, IEqualityComparer<U> equalityComparer = null, CancellationToken cancellationToken = default(CancellationToken), bool cancelImmediately = false)
        where T : class
    {
        Object? unityObject = target as Object;
        bool isUnityObject = target is Object; // don't use (unityObject == null)

        return new UniTask<U>(isUnityObject
            ? WaitUntilValueChangedUnityObjectPromise<T, U>.Create(target, monitorFunction, equalityComparer, monitorTiming, cancellationToken, cancelImmediately, out short token)
            : WaitUntilValueChangedStandardObjectPromise<T, U>.Create(target, monitorFunction, equalityComparer, monitorTiming, cancellationToken, cancelImmediately, out token), token);
    }

    private sealed class WaitUntilPromise : IUniTaskSource, IPlayerLoopItem, ITaskPoolNode<WaitUntilPromise>
    {
        private static TaskPool<WaitUntilPromise> pool;
        private WaitUntilPromise nextNode;
        public ref WaitUntilPromise NextNode => ref nextNode;

        static WaitUntilPromise()
        {
            TaskPool.RegisterSizeGetter(typeof(WaitUntilPromise), () => pool.Size);
        }

        private Func<bool> predicate;
        private CancellationToken cancellationToken;
        private CancellationTokenRegistration cancellationTokenRegistration;
        private bool cancelImmediately;

        private UniTaskCompletionSourceCore<object> core;

        private WaitUntilPromise()
        {
        }

        public static IUniTaskSource Create(Func<bool> predicate, PlayerLoopTiming timing, CancellationToken cancellationToken, bool cancelImmediately, out short token)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
            }

            if (!pool.TryPop(out WaitUntilPromise? result))
            {
                result = new WaitUntilPromise();
            }

            result.predicate = predicate;
            result.cancellationToken = cancellationToken;
            result.cancelImmediately = cancelImmediately;

            if (cancelImmediately && cancellationToken.CanBeCanceled)
            {
                result.cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(state =>
                {
                    WaitUntilPromise? promise = (WaitUntilPromise)state;
                    promise.core.TrySetCanceled(promise.cancellationToken);
                }, result);
            }

            TaskTracker.TrackActiveTask(result, 3);

            PlayerLoopHelper.AddAction(timing, result);

            token = result.core.Version;
            return result;
        }

        public void GetResult(short token)
        {
            try
            {
                core.GetResult(token);
            }
            finally
            {
                if (!(cancelImmediately && cancellationToken.IsCancellationRequested))
                {
                    TryReturn();
                }
            }
        }

        public UniTaskStatus GetStatus(short token)
        {
            return core.GetStatus(token);
        }

        public UniTaskStatus UnsafeGetStatus()
        {
            return core.UnsafeGetStatus();
        }

        public void OnCompleted(Action<object> continuation, object state, short token)
        {
            core.OnCompleted(continuation, state, token);
        }

        public bool MoveNext()
        {
            if (cancellationToken.IsCancellationRequested)
            {
                core.TrySetCanceled(cancellationToken);
                return false;
            }

            try
            {
                if (!predicate())
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                core.TrySetException(ex);
                return false;
            }

            core.TrySetResult(null);
            return false;
        }

        private bool TryReturn()
        {
            TaskTracker.RemoveTracking(this);
            core.Reset();
            predicate = default;
            cancellationToken = default;
            cancellationTokenRegistration.Dispose();
            cancelImmediately = default;
            return pool.TryPush(this);
        }
    }

    private sealed class WaitWhilePromise : IUniTaskSource, IPlayerLoopItem, ITaskPoolNode<WaitWhilePromise>
    {
        private static TaskPool<WaitWhilePromise> pool;
        private WaitWhilePromise nextNode;
        public ref WaitWhilePromise NextNode => ref nextNode;

        static WaitWhilePromise()
        {
            TaskPool.RegisterSizeGetter(typeof(WaitWhilePromise), () => pool.Size);
        }

        private Func<bool> predicate;
        private CancellationToken cancellationToken;
        private CancellationTokenRegistration cancellationTokenRegistration;
        private bool cancelImmediately;

        private UniTaskCompletionSourceCore<object> core;

        private WaitWhilePromise()
        {
        }

        public static IUniTaskSource Create(Func<bool> predicate, PlayerLoopTiming timing, CancellationToken cancellationToken, bool cancelImmediately, out short token)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
            }

            if (!pool.TryPop(out WaitWhilePromise? result))
            {
                result = new WaitWhilePromise();
            }

            result.predicate = predicate;
            result.cancellationToken = cancellationToken;
                
            if (cancelImmediately && cancellationToken.CanBeCanceled)
            {
                result.cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(state =>
                {
                    WaitWhilePromise? promise = (WaitWhilePromise)state;
                    promise.core.TrySetCanceled(promise.cancellationToken);
                }, result);
            }

            TaskTracker.TrackActiveTask(result, 3);

            PlayerLoopHelper.AddAction(timing, result);

            token = result.core.Version;
            return result;
        }

        public void GetResult(short token)
        {
            try
            {
                core.GetResult(token);
            }
            finally
            {
                if (!(cancelImmediately && cancellationToken.IsCancellationRequested))
                {
                    TryReturn();
                }
            }
        }

        public UniTaskStatus GetStatus(short token)
        {
            return core.GetStatus(token);
        }

        public UniTaskStatus UnsafeGetStatus()
        {
            return core.UnsafeGetStatus();
        }

        public void OnCompleted(Action<object> continuation, object state, short token)
        {
            core.OnCompleted(continuation, state, token);
        }

        public bool MoveNext()
        {
            if (cancellationToken.IsCancellationRequested)
            {
                core.TrySetCanceled(cancellationToken);
                return false;
            }

            try
            {
                if (predicate())
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                core.TrySetException(ex);
                return false;
            }

            core.TrySetResult(null);
            return false;
        }

        private bool TryReturn()
        {
            TaskTracker.RemoveTracking(this);
            core.Reset();
            predicate = default;
            cancellationToken = default;
            cancellationTokenRegistration.Dispose();
            cancelImmediately = default;
            return pool.TryPush(this);
        }
    }

    private sealed class WaitUntilCanceledPromise : IUniTaskSource, IPlayerLoopItem, ITaskPoolNode<WaitUntilCanceledPromise>
    {
        private static TaskPool<WaitUntilCanceledPromise> pool;
        private WaitUntilCanceledPromise nextNode;
        public ref WaitUntilCanceledPromise NextNode => ref nextNode;

        static WaitUntilCanceledPromise()
        {
            TaskPool.RegisterSizeGetter(typeof(WaitUntilCanceledPromise), () => pool.Size);
        }

        private CancellationToken cancellationToken;
        private CancellationTokenRegistration cancellationTokenRegistration;
        private bool cancelImmediately;

        private UniTaskCompletionSourceCore<object> core;

        private WaitUntilCanceledPromise()
        {
        }

        public static IUniTaskSource Create(CancellationToken cancellationToken, PlayerLoopTiming timing, bool cancelImmediately, out short token)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
            }

            if (!pool.TryPop(out WaitUntilCanceledPromise? result))
            {
                result = new WaitUntilCanceledPromise();
            }

            result.cancellationToken = cancellationToken;
            result.cancelImmediately = cancelImmediately;

            if (cancelImmediately && cancellationToken.CanBeCanceled)
            {
                result.cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(state =>
                {
                    WaitUntilCanceledPromise? promise = (WaitUntilCanceledPromise)state;
                    promise.core.TrySetResult(null);
                }, result);
            }

            TaskTracker.TrackActiveTask(result, 3);

            PlayerLoopHelper.AddAction(timing, result);

            token = result.core.Version;
            return result;
        }

        public void GetResult(short token)
        {
            try
            {
                core.GetResult(token);
            }
            finally
            {
                if (!(cancelImmediately && cancellationToken.IsCancellationRequested))
                {
                    TryReturn();
                }
            }
        }

        public UniTaskStatus GetStatus(short token)
        {
            return core.GetStatus(token);
        }

        public UniTaskStatus UnsafeGetStatus()
        {
            return core.UnsafeGetStatus();
        }

        public void OnCompleted(Action<object> continuation, object state, short token)
        {
            core.OnCompleted(continuation, state, token);
        }

        public bool MoveNext()
        {
            if (cancellationToken.IsCancellationRequested)
            {
                core.TrySetResult(null);
                return false;
            }

            return true;
        }

        private bool TryReturn()
        {
            TaskTracker.RemoveTracking(this);
            core.Reset();
            cancellationToken = default;
            cancellationTokenRegistration.Dispose();
            cancelImmediately = default;
            return pool.TryPush(this);
        }
    }

    // where T : UnityEngine.Object, can not add constraint
    private sealed class WaitUntilValueChangedUnityObjectPromise<T, U> : IUniTaskSource<U>, IPlayerLoopItem, ITaskPoolNode<WaitUntilValueChangedUnityObjectPromise<T, U>>
    {
        private static TaskPool<WaitUntilValueChangedUnityObjectPromise<T, U>> pool;
        private WaitUntilValueChangedUnityObjectPromise<T, U> nextNode;
        public ref WaitUntilValueChangedUnityObjectPromise<T, U> NextNode => ref nextNode;

        static WaitUntilValueChangedUnityObjectPromise()
        {
            TaskPool.RegisterSizeGetter(typeof(WaitUntilValueChangedUnityObjectPromise<T, U>), () => pool.Size);
        }

        private T target;
        private Object targetAsUnityObject;
        private U currentValue;
        private Func<T, U> monitorFunction;
        private IEqualityComparer<U> equalityComparer;
        private CancellationToken cancellationToken;
        private CancellationTokenRegistration cancellationTokenRegistration;
        private bool cancelImmediately;

        private UniTaskCompletionSourceCore<U> core;

        private WaitUntilValueChangedUnityObjectPromise()
        {
        }

        public static IUniTaskSource<U> Create(T target, Func<T, U> monitorFunction, IEqualityComparer<U> equalityComparer, PlayerLoopTiming timing, CancellationToken cancellationToken, bool cancelImmediately, out short token)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return AutoResetUniTaskCompletionSource<U>.CreateFromCanceled(cancellationToken, out token);
            }

            if (!pool.TryPop(out WaitUntilValueChangedUnityObjectPromise<T, U>? result))
            {
                result = new WaitUntilValueChangedUnityObjectPromise<T, U>();
            }

            result.target = target;
            result.targetAsUnityObject = target as Object;
            result.monitorFunction = monitorFunction;
            result.currentValue = monitorFunction(target);
            result.equalityComparer = equalityComparer ?? UnityEqualityComparer.GetDefault<U>();
            result.cancellationToken = cancellationToken;
            result.cancelImmediately = cancelImmediately;
                
            if (cancelImmediately && cancellationToken.CanBeCanceled)
            {
                result.cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(state =>
                {
                    WaitUntilValueChangedUnityObjectPromise<T, U>? promise = (WaitUntilValueChangedUnityObjectPromise<T, U>)state;
                    promise.core.TrySetCanceled(promise.cancellationToken);
                }, result);
            }

            TaskTracker.TrackActiveTask(result, 3);

            PlayerLoopHelper.AddAction(timing, result);

            token = result.core.Version;
            return result;
        }

        public U GetResult(short token)
        {
            try
            {
                return core.GetResult(token);
            }
            finally
            {
                if (!(cancelImmediately && cancellationToken.IsCancellationRequested))
                {
                    TryReturn();
                }
            }
        }

        void IUniTaskSource.GetResult(short token)
        {
            GetResult(token);
        }

        public UniTaskStatus GetStatus(short token)
        {
            return core.GetStatus(token);
        }

        public UniTaskStatus UnsafeGetStatus()
        {
            return core.UnsafeGetStatus();
        }

        public void OnCompleted(Action<object> continuation, object state, short token)
        {
            core.OnCompleted(continuation, state, token);
        }

        public bool MoveNext()
        {
            if (cancellationToken.IsCancellationRequested || targetAsUnityObject == null) // destroyed = cancel.
            {
                core.TrySetCanceled(cancellationToken);
                return false;
            }

            U nextValue = default(U);
            try
            {
                nextValue = monitorFunction(target);
                if (equalityComparer.Equals(currentValue, nextValue))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                core.TrySetException(ex);
                return false;
            }

            core.TrySetResult(nextValue);
            return false;
        }

        private bool TryReturn()
        {
            TaskTracker.RemoveTracking(this);
            core.Reset();
            target = default;
            currentValue = default;
            monitorFunction = default;
            equalityComparer = default;
            cancellationToken = default;
            cancellationTokenRegistration.Dispose();
            cancelImmediately = default;
            return pool.TryPush(this);
        }
    }

    private sealed class WaitUntilValueChangedStandardObjectPromise<T, U> : IUniTaskSource<U>, IPlayerLoopItem, ITaskPoolNode<WaitUntilValueChangedStandardObjectPromise<T, U>>
        where T : class
    {
        private static TaskPool<WaitUntilValueChangedStandardObjectPromise<T, U>> pool;
        private WaitUntilValueChangedStandardObjectPromise<T, U> nextNode;
        public ref WaitUntilValueChangedStandardObjectPromise<T, U> NextNode => ref nextNode;

        static WaitUntilValueChangedStandardObjectPromise()
        {
            TaskPool.RegisterSizeGetter(typeof(WaitUntilValueChangedStandardObjectPromise<T, U>), () => pool.Size);
        }

        private WeakReference<T> target;
        private U currentValue;
        private Func<T, U> monitorFunction;
        private IEqualityComparer<U> equalityComparer;
        private CancellationToken cancellationToken;
        private CancellationTokenRegistration cancellationTokenRegistration;
        private bool cancelImmediately;

        private UniTaskCompletionSourceCore<U> core;

        private WaitUntilValueChangedStandardObjectPromise()
        {
        }

        public static IUniTaskSource<U> Create(T target, Func<T, U> monitorFunction, IEqualityComparer<U> equalityComparer, PlayerLoopTiming timing, CancellationToken cancellationToken, bool cancelImmediately, out short token)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return AutoResetUniTaskCompletionSource<U>.CreateFromCanceled(cancellationToken, out token);
            }

            if (!pool.TryPop(out WaitUntilValueChangedStandardObjectPromise<T, U>? result))
            {
                result = new WaitUntilValueChangedStandardObjectPromise<T, U>();
            }

            result.target = new WeakReference<T>(target, false); // wrap in WeakReference.
            result.monitorFunction = monitorFunction;
            result.currentValue = monitorFunction(target);
            result.equalityComparer = equalityComparer ?? UnityEqualityComparer.GetDefault<U>();
            result.cancellationToken = cancellationToken;
            result.cancelImmediately = cancelImmediately;
                
            if (cancelImmediately && cancellationToken.CanBeCanceled)
            {
                result.cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(state =>
                {
                    WaitUntilValueChangedStandardObjectPromise<T, U>? promise = (WaitUntilValueChangedStandardObjectPromise<T, U>)state;
                    promise.core.TrySetCanceled(promise.cancellationToken);
                }, result);
            }

            TaskTracker.TrackActiveTask(result, 3);

            PlayerLoopHelper.AddAction(timing, result);

            token = result.core.Version;
            return result;
        }

        public U GetResult(short token)
        {
            try
            {
                return core.GetResult(token);
            }
            finally
            {
                if (!(cancelImmediately && cancellationToken.IsCancellationRequested))
                {
                    TryReturn();
                }
            }
        }

        void IUniTaskSource.GetResult(short token)
        {
            GetResult(token);
        }

        public UniTaskStatus GetStatus(short token)
        {
            return core.GetStatus(token);
        }

        public UniTaskStatus UnsafeGetStatus()
        {
            return core.UnsafeGetStatus();
        }

        public void OnCompleted(Action<object> continuation, object state, short token)
        {
            core.OnCompleted(continuation, state, token);
        }

        public bool MoveNext()
        {
            if (cancellationToken.IsCancellationRequested || !target.TryGetTarget(out T? t)) // doesn't find = cancel.
            {
                core.TrySetCanceled(cancellationToken);
                return false;
            }

            U nextValue = default(U);
            try
            {
                nextValue = monitorFunction(t);
                if (equalityComparer.Equals(currentValue, nextValue))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                core.TrySetException(ex);
                return false;
            }

            core.TrySetResult(nextValue);
            return false;
        }

        private bool TryReturn()
        {
            TaskTracker.RemoveTracking(this);
            core.Reset();
            target = default;
            currentValue = default;
            monitorFunction = default;
            equalityComparer = default;
            cancellationToken = default;
            cancellationTokenRegistration.Dispose();
            cancelImmediately = default;
            return pool.TryPush(this);
        }
    }
}