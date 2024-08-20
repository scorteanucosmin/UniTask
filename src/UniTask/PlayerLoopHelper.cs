#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks.Internal;
using System.Threading;
using UnityEngine.LowLevel;
using PlayerLoopType = UnityEngine.PlayerLoop;

namespace Cysharp.Threading.Tasks;

public static class UniTaskLoopRunners
{
    public struct UniTaskLoopRunnerInitialization { };
    public struct UniTaskLoopRunnerEarlyUpdate { };
    public struct UniTaskLoopRunnerFixedUpdate { };
    public struct UniTaskLoopRunnerPreUpdate { };
    public struct UniTaskLoopRunnerUpdate { };
    public struct UniTaskLoopRunnerPreLateUpdate { };
    public struct UniTaskLoopRunnerPostLateUpdate { };

    // Last

    public struct UniTaskLoopRunnerLastInitialization { };
    public struct UniTaskLoopRunnerLastEarlyUpdate { };
    public struct UniTaskLoopRunnerLastFixedUpdate { };
    public struct UniTaskLoopRunnerLastPreUpdate { };
    public struct UniTaskLoopRunnerLastUpdate { };
    public struct UniTaskLoopRunnerLastPreLateUpdate { };
    public struct UniTaskLoopRunnerLastPostLateUpdate { };

    // Yield

    public struct UniTaskLoopRunnerYieldInitialization { };
    public struct UniTaskLoopRunnerYieldEarlyUpdate { };
    public struct UniTaskLoopRunnerYieldFixedUpdate { };
    public struct UniTaskLoopRunnerYieldPreUpdate { };
    public struct UniTaskLoopRunnerYieldUpdate { };
    public struct UniTaskLoopRunnerYieldPreLateUpdate { };
    public struct UniTaskLoopRunnerYieldPostLateUpdate { };

    // Yield Last

    public struct UniTaskLoopRunnerLastYieldInitialization { };
    public struct UniTaskLoopRunnerLastYieldEarlyUpdate { };
    public struct UniTaskLoopRunnerLastYieldFixedUpdate { };
    public struct UniTaskLoopRunnerLastYieldPreUpdate { };
    public struct UniTaskLoopRunnerLastYieldUpdate { };
    public struct UniTaskLoopRunnerLastYieldPreLateUpdate { };
    public struct UniTaskLoopRunnerLastYieldPostLateUpdate { };
        
    // TimeUpdate
    public struct UniTaskLoopRunnerTimeUpdate { };
    public struct UniTaskLoopRunnerLastTimeUpdate { };
    public struct UniTaskLoopRunnerYieldTimeUpdate { };
    public struct UniTaskLoopRunnerLastYieldTimeUpdate { };

}

public enum PlayerLoopTiming
{
    Initialization = 0,
    LastInitialization = 1,

    EarlyUpdate = 2,
    LastEarlyUpdate = 3,

    FixedUpdate = 4,
    LastFixedUpdate = 5,

    PreUpdate = 6,
    LastPreUpdate = 7,

    Update = 8,
    LastUpdate = 9,

    PreLateUpdate = 10,
    LastPreLateUpdate = 11,

    PostLateUpdate = 12,
    LastPostLateUpdate = 13,
    // Unity 2020.2 added TimeUpdate https://docs.unity3d.com/2020.2/Documentation/ScriptReference/PlayerLoop.TimeUpdate.html
    TimeUpdate = 14,
    LastTimeUpdate = 15,
}

[Flags]
public enum InjectPlayerLoopTimings
{
    /// <summary>
    /// Preset: All loops(default).
    /// </summary>
    All =
        Initialization | LastInitialization |
        EarlyUpdate | LastEarlyUpdate |
        FixedUpdate | LastFixedUpdate |
        PreUpdate | LastPreUpdate |
        Update | LastUpdate |
        PreLateUpdate | LastPreLateUpdate |
        PostLateUpdate | LastPostLateUpdate | 
        TimeUpdate | LastTimeUpdate,


    /// <summary>
    /// Preset: All without last except LastPostLateUpdate.
    /// </summary>
    Standard =
        Initialization |
        EarlyUpdate |
        FixedUpdate |
        PreUpdate |
        Update |
        PreLateUpdate |
        PostLateUpdate | LastPostLateUpdate | 
        TimeUpdate,
    /// <summary>
    /// Preset: Minimum pattern, Update | FixedUpdate | LastPostLateUpdate
    /// </summary>
    Minimum = Update | FixedUpdate | LastPostLateUpdate,

    // PlayerLoopTiming

    Initialization = 1,
    LastInitialization = 2,

    EarlyUpdate = 4,
    LastEarlyUpdate = 8,

    FixedUpdate = 16,
    LastFixedUpdate = 32,

    PreUpdate = 64,
    LastPreUpdate = 128,

    Update = 256,
    LastUpdate = 512,

    PreLateUpdate = 1024,
    LastPreLateUpdate = 2048,

    PostLateUpdate = 4096,
    LastPostLateUpdate = 8192,
    // Unity 2020.2 added TimeUpdate https://docs.unity3d.com/2020.2/Documentation/ScriptReference/PlayerLoop.TimeUpdate.html
    TimeUpdate = 16384,
    LastTimeUpdate = 32768

}

public interface IPlayerLoopItem
{
    bool MoveNext();
}

public static class PlayerLoopHelper
{
    public static int MainThreadId { get; private set; }
    public static SynchronizationContext UnitySynchronizationContext { get; private set; }
    
    internal static string ApplicationDataPath { get; }
    public static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == MainThreadId;
    private static ContinuationQueue[] _yielders;
    private static PlayerLoopRunner[] _runners;
    
    private static PlayerLoopSystem[] InsertRunner(PlayerLoopSystem loopSystem,
        bool injectOnFirst,
        Type loopRunnerYieldType, ContinuationQueue cq,
        Type loopRunnerType, PlayerLoopRunner runner)
    {
        PlayerLoopSystem yieldLoop = new()
        {
            type = loopRunnerYieldType,
            updateDelegate = cq.Run
        };

        PlayerLoopSystem runnerLoop = new()
        {
            type = loopRunnerType,
            updateDelegate = runner.Run
        };

        // Remove items from previous initializations.
        PlayerLoopSystem[] source = RemoveRunner(loopSystem, loopRunnerYieldType, loopRunnerType);
        PlayerLoopSystem[] dest = new PlayerLoopSystem[source.Length + 2];

        Array.Copy(source, 0, dest, injectOnFirst ? 2 : 0, source.Length);
        if (injectOnFirst)
        {
            dest[0] = yieldLoop;
            dest[1] = runnerLoop;
        }
        else
        {
            dest[dest.Length - 2] = yieldLoop;
            dest[dest.Length - 1] = runnerLoop;
        }

        return dest;
    }

    private static PlayerLoopSystem[] RemoveRunner(PlayerLoopSystem loopSystem, Type loopRunnerYieldType, Type loopRunnerType)
    {
        return loopSystem.subSystemList
            .Where(ls => ls.type != loopRunnerYieldType && ls.type != loopRunnerType)
            .ToArray();
    }

    private static PlayerLoopSystem[] InsertUniTaskSynchronizationContext(PlayerLoopSystem loopSystem)
    {
        PlayerLoopSystem loop = new()
        {
            type = typeof(UniTaskSynchronizationContext),
            updateDelegate = UniTaskSynchronizationContext.Run
        };

        // Remove items from previous initializations.
        PlayerLoopSystem[] source = loopSystem.subSystemList
            .Where(ls => ls.type != typeof(UniTaskSynchronizationContext))
            .ToArray();

        List<PlayerLoopSystem> dest = new(source);

        int index = dest.FindIndex(x => x.type.Name == "ScriptRunDelayedTasks");
        if (index == -1)
        {
            index = dest.FindIndex(x => x.type.Name == "UniTaskLoopRunnerUpdate");
        }

        dest.Insert(index + 1, loop);

        return dest.ToArray();
    }

    private static int FindLoopSystemIndex(PlayerLoopSystem[] playerLoopList, Type systemType)
    {
        for (int i = 0; i < playerLoopList.Length; i++)
        {
            if (playerLoopList[i].type == systemType)
            {
                return i;
            }
        }

        throw new Exception("Target PlayerLoopSystem does not found. Type:" + systemType.FullName);
    }

    private static void InsertLoop(PlayerLoopSystem[] copyList, InjectPlayerLoopTimings injectTimings, Type loopType, InjectPlayerLoopTimings targetTimings,
        int index, bool injectOnFirst, Type loopRunnerYieldType, Type loopRunnerType, PlayerLoopTiming playerLoopTiming)
    {
        int i = FindLoopSystemIndex(copyList, loopType);
        if ((injectTimings & targetTimings) == targetTimings)
        {
            copyList[i].subSystemList = InsertRunner(copyList[i], injectOnFirst,
                loopRunnerYieldType, _yielders[index] = new ContinuationQueue(playerLoopTiming),
                loopRunnerType, _runners[index] = new PlayerLoopRunner(playerLoopTiming));
        }
        else
        {
            copyList[i].subSystemList = RemoveRunner(copyList[i], loopRunnerYieldType, loopRunnerType);
        }
    }

    public static void Initialize(ref PlayerLoopSystem playerLoop, InjectPlayerLoopTimings injectTimings = InjectPlayerLoopTimings.All)
    {
        _yielders = new ContinuationQueue[16];
        _runners = new PlayerLoopRunner[16];

        PlayerLoopSystem[] copyList = playerLoop.subSystemList.ToArray();

        // Initialization
        InsertLoop(copyList, injectTimings, typeof(PlayerLoopType.Initialization),
            InjectPlayerLoopTimings.Initialization, 0, true,
            typeof(UniTaskLoopRunners.UniTaskLoopRunnerYieldInitialization), typeof(UniTaskLoopRunners.UniTaskLoopRunnerInitialization), PlayerLoopTiming.Initialization);

        InsertLoop(copyList, injectTimings, typeof(PlayerLoopType.Initialization),
            InjectPlayerLoopTimings.LastInitialization, 1, false,
            typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastYieldInitialization), typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastInitialization), PlayerLoopTiming.LastInitialization);

        // EarlyUpdate
        InsertLoop(copyList, injectTimings, typeof(PlayerLoopType.EarlyUpdate),
            InjectPlayerLoopTimings.EarlyUpdate, 2, true,
            typeof(UniTaskLoopRunners.UniTaskLoopRunnerYieldEarlyUpdate), typeof(UniTaskLoopRunners.UniTaskLoopRunnerEarlyUpdate), PlayerLoopTiming.EarlyUpdate);

        InsertLoop(copyList, injectTimings, typeof(PlayerLoopType.EarlyUpdate),
            InjectPlayerLoopTimings.LastEarlyUpdate, 3, false,
            typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastYieldEarlyUpdate), typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastEarlyUpdate), PlayerLoopTiming.LastEarlyUpdate);

        // FixedUpdate
        InsertLoop(copyList, injectTimings, typeof(PlayerLoopType.FixedUpdate),
            InjectPlayerLoopTimings.FixedUpdate, 4, true,
            typeof(UniTaskLoopRunners.UniTaskLoopRunnerYieldFixedUpdate), typeof(UniTaskLoopRunners.UniTaskLoopRunnerFixedUpdate), PlayerLoopTiming.FixedUpdate);

        InsertLoop(copyList, injectTimings, typeof(PlayerLoopType.FixedUpdate),
            InjectPlayerLoopTimings.LastFixedUpdate, 5, false,
            typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastYieldFixedUpdate), typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastFixedUpdate), PlayerLoopTiming.LastFixedUpdate);

        // PreUpdate
        InsertLoop(copyList, injectTimings, typeof(PlayerLoopType.PreUpdate),
            InjectPlayerLoopTimings.PreUpdate, 6, true,
            typeof(UniTaskLoopRunners.UniTaskLoopRunnerYieldPreUpdate), typeof(UniTaskLoopRunners.UniTaskLoopRunnerPreUpdate), PlayerLoopTiming.PreUpdate);

        InsertLoop(copyList, injectTimings, typeof(PlayerLoopType.PreUpdate),
            InjectPlayerLoopTimings.LastPreUpdate, 7, false,
            typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastYieldPreUpdate), typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastPreUpdate), PlayerLoopTiming.LastPreUpdate);

        // Update
        InsertLoop(copyList, injectTimings, typeof(PlayerLoopType.Update),
            InjectPlayerLoopTimings.Update, 8, true,
            typeof(UniTaskLoopRunners.UniTaskLoopRunnerYieldUpdate), typeof(UniTaskLoopRunners.UniTaskLoopRunnerUpdate), PlayerLoopTiming.Update);

        InsertLoop(copyList, injectTimings, typeof(PlayerLoopType.Update),
            InjectPlayerLoopTimings.LastUpdate, 9, false,
            typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastYieldUpdate), typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastUpdate), PlayerLoopTiming.LastUpdate);

        // PreLateUpdate
        InsertLoop(copyList, injectTimings, typeof(PlayerLoopType.PreLateUpdate),
            InjectPlayerLoopTimings.PreLateUpdate, 10, true,
            typeof(UniTaskLoopRunners.UniTaskLoopRunnerYieldPreLateUpdate), typeof(UniTaskLoopRunners.UniTaskLoopRunnerPreLateUpdate), PlayerLoopTiming.PreLateUpdate);

        InsertLoop(copyList, injectTimings, typeof(PlayerLoopType.PreLateUpdate),
            InjectPlayerLoopTimings.LastPreLateUpdate, 11, false,
            typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastYieldPreLateUpdate), typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastPreLateUpdate), PlayerLoopTiming.LastPreLateUpdate);

        // PostLateUpdate
        InsertLoop(copyList, injectTimings, typeof(PlayerLoopType.PostLateUpdate),
            InjectPlayerLoopTimings.PostLateUpdate, 12, true,
            typeof(UniTaskLoopRunners.UniTaskLoopRunnerYieldPostLateUpdate), typeof(UniTaskLoopRunners.UniTaskLoopRunnerPostLateUpdate), PlayerLoopTiming.PostLateUpdate);

        InsertLoop(copyList, injectTimings, typeof(PlayerLoopType.PostLateUpdate),
            InjectPlayerLoopTimings.LastPostLateUpdate, 13, false,
            typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastYieldPostLateUpdate), typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastPostLateUpdate), PlayerLoopTiming.LastPostLateUpdate);
            
        // TimeUpdate
        InsertLoop(copyList, injectTimings, typeof(PlayerLoopType.TimeUpdate),
            InjectPlayerLoopTimings.TimeUpdate, 14, true,
            typeof(UniTaskLoopRunners.UniTaskLoopRunnerYieldTimeUpdate), typeof(UniTaskLoopRunners.UniTaskLoopRunnerTimeUpdate), PlayerLoopTiming.TimeUpdate);

        InsertLoop(copyList, injectTimings, typeof(PlayerLoopType.TimeUpdate),
            InjectPlayerLoopTimings.LastTimeUpdate, 15, false,
            typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastYieldTimeUpdate), typeof(UniTaskLoopRunners.UniTaskLoopRunnerLastTimeUpdate), PlayerLoopTiming.LastTimeUpdate);

        // Insert UniTaskSynchronizationContext to Update loop
        int i = FindLoopSystemIndex(copyList, typeof(PlayerLoopType.Update));
        copyList[i].subSystemList = InsertUniTaskSynchronizationContext(copyList[i]);

        playerLoop.subSystemList = copyList;
        PlayerLoop.SetPlayerLoop(playerLoop);
    }
    
    private static void ThrowInvalidLoopTiming(PlayerLoopTiming playerLoopTiming)
    {
        throw new InvalidOperationException("Target playerLoopTiming is not injected. Please check PlayerLoopHelper.Initialize. PlayerLoopTiming:" + playerLoopTiming);
    }

    public static void AddAction(PlayerLoopTiming timing, IPlayerLoopItem action)
    {
        PlayerLoopRunner runner = _runners[(int)timing];
        if (runner == null)
        {
            ThrowInvalidLoopTiming(timing);
        }
            
        runner.AddAction(action);
    }

    public static void AddContinuation(PlayerLoopTiming timing, Action continuation)
    {
        ContinuationQueue continuationQueue = _yielders[(int)timing];
        if (continuationQueue == null)
        {
            ThrowInvalidLoopTiming(timing);
        }
            
        continuationQueue.Enqueue(continuation);
    }

    public static bool IsInjectedUniTaskPlayerLoop()
    {
        PlayerLoopSystem playerLoop = PlayerLoop.GetCurrentPlayerLoop();
        foreach (PlayerLoopSystem header in playerLoop.subSystemList)
        {
            if (header.subSystemList is null) 
            { 
                continue;
            }
                
            foreach (PlayerLoopSystem subSystem in header.subSystemList)
            {
                if (subSystem.type == typeof(UniTaskLoopRunners.UniTaskLoopRunnerInitialization))
                {
                    return true;
                }
            }
        }

        return false;
    }
    
    public static void SetMainThreadId(int threadId)
    {
        MainThreadId = threadId;
    }
    
    public static void SetSynchronizationContext(SynchronizationContext synchronizationContext)
    {
        UnitySynchronizationContext = synchronizationContext;
    }
}