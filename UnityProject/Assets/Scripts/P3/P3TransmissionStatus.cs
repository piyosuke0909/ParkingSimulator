using System;
using UnityEngine;

public class P3TransmissionStatus : MonoBehaviour
{
    [Header("Connection (Read Only)")]
    [SerializeField]
    private bool backendReachable;

    [SerializeField]
    private string lastHealthCheckUtc;

    [SerializeField]
    private string lastHealthError;

    [Header("Queue (Read Only)")]
    [SerializeField]
    private int pendingSnapshotCount;

    [SerializeField]
    private int pendingEventCount;

    [SerializeField]
    private bool snapshotRequestInProgress;

    [SerializeField]
    private bool eventRequestInProgress;

    [Header("Totals (Read Only)")]
    [SerializeField]
    private long transmittedSnapshotCount;

    [SerializeField]
    private long transmittedEventCount;

    [SerializeField]
    private long permanentlyFailedSnapshotCount;

    [SerializeField]
    private long permanentlyFailedEventCount;

    [Header("Last Request (Read Only)")]
    [SerializeField]
    private string lastSuccessUtc;

    [SerializeField]
    private string lastFailureUtc;

    [SerializeField]
    private long lastHttpStatusCode;

    [SerializeField]
    [TextArea(2, 6)]
    private string lastError;

    public bool BackendReachable => backendReachable;
    public int PendingSnapshotCount => pendingSnapshotCount;
    public int PendingEventCount => pendingEventCount;
    public long TransmittedSnapshotCount => transmittedSnapshotCount;
    public long TransmittedEventCount => transmittedEventCount;
    public string LastError => lastError;

    public void SetPendingSnapshotCount(int value)
    {
        pendingSnapshotCount = Mathf.Max(0, value);
    }

    public void SetPendingEventCount(int value)
    {
        pendingEventCount = Mathf.Max(0, value);
    }

    public void SetSnapshotRequestInProgress(bool value)
    {
        snapshotRequestInProgress = value;
    }

    public void SetEventRequestInProgress(bool value)
    {
        eventRequestInProgress = value;
    }

    public void RecordHealth(bool reachable, long statusCode, string error)
    {
        backendReachable = reachable;
        lastHealthCheckUtc = DateTimeOffset.UtcNow.ToString("o");
        lastHealthError = reachable ? string.Empty : NullToEmpty(error);
        lastHttpStatusCode = statusCode;

        if (!reachable && !string.IsNullOrWhiteSpace(error))
        {
            RecordFailure(statusCode, error);
        }
    }

    public void RecordSnapshotSuccess(int count, long statusCode)
    {
        transmittedSnapshotCount += Math.Max(0, count);
        RecordSuccess(statusCode);
    }

    public void RecordEventSuccess(int count, long statusCode)
    {
        transmittedEventCount += Math.Max(0, count);
        RecordSuccess(statusCode);
    }

    public void RecordSnapshotPermanentFailure(int count, long statusCode, string error)
    {
        permanentlyFailedSnapshotCount += Math.Max(0, count);
        RecordFailure(statusCode, error);
    }

    public void RecordEventPermanentFailure(int count, long statusCode, string error)
    {
        permanentlyFailedEventCount += Math.Max(0, count);
        RecordFailure(statusCode, error);
    }

    public void RecordTransientFailure(long statusCode, string error)
    {
        backendReachable = false;
        RecordFailure(statusCode, error);
    }

    private void RecordSuccess(long statusCode)
    {
        backendReachable = true;
        lastSuccessUtc = DateTimeOffset.UtcNow.ToString("o");
        lastHttpStatusCode = statusCode;
        lastError = string.Empty;
    }

    private void RecordFailure(long statusCode, string error)
    {
        lastFailureUtc = DateTimeOffset.UtcNow.ToString("o");
        lastHttpStatusCode = statusCode;
        lastError = NullToEmpty(error);
    }

    private static string NullToEmpty(string value)
    {
        return value ?? string.Empty;
    }
}
