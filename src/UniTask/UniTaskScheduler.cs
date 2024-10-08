﻿#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System;
using System.Threading;

namespace Cysharp.Threading.Tasks;
// UniTask has no scheduler like TaskScheduler.
// Only handle unobserved exception.

public static class UniTaskScheduler
{
    public static event Action<Exception> UnobservedTaskException;

    /// <summary>
    /// Propagate OperationCanceledException to UnobservedTaskException when true. Default is false.
    /// </summary>
    public static bool PropagateOperationCanceledException = false;

    /// <summary>
    /// Write log type when catch unobserved exception and not registered UnobservedTaskException. Default is Exception.
    /// </summary>
    public static UnityEngine.LogType UnobservedExceptionWriteLogType = UnityEngine.LogType.Exception;

    /// <summary>
    /// Dispatch exception event to Unity MainThread. Default is false.
    /// </summary>
    public static bool DispatchUnityMainThread = false;

    // cache delegate.
    private static readonly SendOrPostCallback HandleExceptionInvoke = InvokeUnobservedTaskException;

    private static void InvokeUnobservedTaskException(object state)
    {
        UnobservedTaskException((Exception)state);
    }

    internal static void PublishUnobservedTaskException(Exception exception)
    {
        if (exception == null)
        {
            return;
        }
        
        if (!PropagateOperationCanceledException && exception is OperationCanceledException)
        {
            return;
        }

        if (UnobservedTaskException != null)
        {
            if (!DispatchUnityMainThread || Thread.CurrentThread.ManagedThreadId == PlayerLoopHelper.MainThreadId)
            {
                // allows inlining call.
                UnobservedTaskException.Invoke(exception);
            }
            else
            {
                // Post to MainThread.
                PlayerLoopHelper.UnitySynchronizationContext.Post(HandleExceptionInvoke, exception);
            }
        }
        else
        {
            string message = null;
            if (UnobservedExceptionWriteLogType != UnityEngine.LogType.Exception)
            {
                message = string.Format("UnobservedTaskException: {0}", exception);
            }

            switch (UnobservedExceptionWriteLogType)
            {
                case UnityEngine.LogType.Error:
                {
                    UnityEngine.Debug.LogError(message);
                    break;
                }
                case UnityEngine.LogType.Assert:
                {
                    UnityEngine.Debug.LogAssertion(message);
                    break;
                }
                case UnityEngine.LogType.Warning:
                {
                    UnityEngine.Debug.LogWarning(message);
                    break;
                }
                case UnityEngine.LogType.Log:
                {
                    UnityEngine.Debug.Log(message);
                    break;
                }
                case UnityEngine.LogType.Exception:
                {
                    UnityEngine.Debug.LogException(exception);
                    break;
                }
                default:
                {
                    break;
                }
            }
        }
    }
}