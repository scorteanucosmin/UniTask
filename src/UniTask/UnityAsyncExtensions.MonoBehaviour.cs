using System;
using System.Threading;

namespace Cysharp.Threading.Tasks;

public static partial class UnityAsyncExtensions
{
    public static UniTask StartAsyncCoroutine(this UnityEngine.MonoBehaviour monoBehaviour, Func<CancellationToken, UniTask> asyncCoroutine)
    {
        CancellationToken token = monoBehaviour.GetCancellationTokenOnDestroy();
        return asyncCoroutine(token);
    }
}