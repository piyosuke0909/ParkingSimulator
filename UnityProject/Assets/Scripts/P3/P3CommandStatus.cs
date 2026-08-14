using System;
using UnityEngine;

public class P3CommandStatus : MonoBehaviour
{
    [Header("Polling (Read Only)")]
    [SerializeField] private bool requestInProgress;
    [SerializeField] private int consecutiveFailures;
    [SerializeField] private float currentPollingDelaySeconds;
    [SerializeField] private long lastHttpStatusCode;
    [SerializeField] private string lastPollUtc;
    [SerializeField] private string lastSuccessfulPollUtc;
    [SerializeField, TextArea(2, 5)] private string lastError;

    [Header("Commands (Read Only)")]
    [SerializeField] private long receivedCommandCount;
    [SerializeField] private long succeededCommandCount;
    [SerializeField] private long rejectedCommandCount;
    [SerializeField] private long failedCommandCount;
    [SerializeField] private long expiredCommandCount;
    [SerializeField] private string lastCommandId;
    [SerializeField] private string lastCommandType;
    [SerializeField] private string lastCommandResult;

    public bool RequestInProgress => requestInProgress;
    public int ConsecutiveFailures => consecutiveFailures;
    public string LastError => lastError;

    public void SetRequestInProgress(bool value)
    {
        requestInProgress = value;
    }

    public void RecordPollSuccess(long statusCode, float nextDelaySeconds)
    {
        consecutiveFailures = 0;
        currentPollingDelaySeconds = nextDelaySeconds;
        lastHttpStatusCode = statusCode;
        lastPollUtc = DateTimeOffset.UtcNow.ToString("o");
        lastSuccessfulPollUtc = lastPollUtc;
        lastError = string.Empty;
    }

    public void RecordPollFailure(long statusCode, string error, int failures, float nextDelaySeconds)
    {
        consecutiveFailures = Mathf.Max(0, failures);
        currentPollingDelaySeconds = Mathf.Max(0f, nextDelaySeconds);
        lastHttpStatusCode = statusCode;
        lastPollUtc = DateTimeOffset.UtcNow.ToString("o");
        lastError = error ?? string.Empty;
    }

    public void RecordReceived(P3Command command)
    {
        receivedCommandCount++;
        Remember(command, "RECEIVED");
    }

    public void RecordSucceeded(P3Command command)
    {
        succeededCommandCount++;
        Remember(command, "SUCCEEDED");
    }

    public void RecordRejected(P3Command command, string reasonCode)
    {
        rejectedCommandCount++;
        Remember(command, "REJECTED:" + (reasonCode ?? string.Empty));
    }

    public void RecordFailed(P3Command command, string reasonCode)
    {
        failedCommandCount++;
        Remember(command, "FAILED:" + (reasonCode ?? string.Empty));
    }

    public void RecordExpired(P3Command command)
    {
        expiredCommandCount++;
        Remember(command, "EXPIRED");
    }

    private void Remember(P3Command command, string result)
    {
        lastCommandId = command != null ? command.commandId ?? string.Empty : string.Empty;
        lastCommandType = command != null ? command.commandType ?? string.Empty : string.Empty;
        lastCommandResult = result ?? string.Empty;
    }
}
