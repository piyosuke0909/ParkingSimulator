using System;
using UnityEngine;

/// <summary>
/// Snapshot と Event が同じ実行単位を参照するための共有コンテキストです。
/// Play セッションごとに sessionId を1つ生成し、シナリオ実行ごとに runId を更新します。
/// </summary>
public static class P2SimulationRunContext
{
    private static string sessionId;
    private static string currentRunId;
    private static int runSequence;

    public static string SessionId => EnsureSessionId();
    public static string CurrentRunId => currentRunId ?? string.Empty;
    public static int RunSequence => runSequence;
    public static bool HasCurrentRun => !string.IsNullOrWhiteSpace(currentRunId);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlaySession()
    {
        sessionId = string.Empty;
        currentRunId = string.Empty;
        runSequence = 0;
    }

    public static P2SimulationRunIdentity EnsureCurrentRun()
    {
        EnsureSessionId();

        if (!HasCurrentRun)
        {
            return BeginNewRun();
        }

        return GetCurrentIdentity();
    }

    public static P2SimulationRunIdentity BeginNewRun()
    {
        EnsureSessionId();

        runSequence++;
        currentRunId = sessionId + "-run-" + runSequence.ToString("D4");
        return GetCurrentIdentity();
    }

    public static P2SimulationRunIdentity GetCurrentIdentity()
    {
        return new P2SimulationRunIdentity
        {
            sessionId = EnsureSessionId(),
            runId = CurrentRunId,
            runSequence = runSequence
        };
    }

    private static string EnsureSessionId()
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            sessionId = "unity-session-" +
                        DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
        }

        return sessionId;
    }
}

[Serializable]
public struct P2SimulationRunIdentity
{
    public string sessionId;
    public string runId;
    public int runSequence;
}
