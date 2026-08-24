using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

[DefaultExecutionOrder(-300)]
public class P3CommandPollingClient : MonoBehaviour
{
    public const int DefaultMaximumIdempotencyCacheEntries = 1024;

    public P3BackendSettings settings;
    public P3CommandStatus commandStatus;
    public P3CommandContractValidator contractValidator;
    public P3AreaPolicyRuntime areaPolicyRuntime;
    public P2SimulationEventPublisher eventPublisher;

    [Header("Idempotency Cache")]
    [Min(1)]
    [Tooltip("Maximum number of processed Command idempotency keys retained for the current run. Oldest entries are evicted first.")]
    public int maximumIdempotencyCacheEntries = DefaultMaximumIdempotencyCacheEntries;

    [Header("Logging")]
    public bool logSuccessfulPolls = false;
    public bool logCommands = true;
    public bool logFailures = true;

    private readonly HashSet<string> processedIdempotencyKeys =
        new HashSet<string>(StringComparer.Ordinal);

    private readonly Dictionary<string, CachedTerminalResult> terminalResults =
        new Dictionary<string, CachedTerminalResult>(StringComparer.Ordinal);

    private readonly Queue<string> idempotencyInsertionOrder =
        new Queue<string>();

    private string idempotencyCacheRunId = string.Empty;
    private Coroutine pollingCoroutine;
    private int consecutiveFailures;
    private bool hasStarted;

    private sealed class CachedTerminalResult
    {
        public string eventType;
        public string status;
        public string reasonCode;
        public string message;
        public bool retryable;
        public string areaId;
        public string previousPolicy;
        public string appliedPolicy;
    }

    private void Awake()
    {
        // Keep the simulation and P3 command polling active while the Unity window
        // is not focused (for example, while operating the browser admin UI).
        Application.runInBackground = true;
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (hasStarted)
        {
            StartPollingIfNeeded();
        }
    }

    private void Start()
    {
        hasStarted = true;
        ResolveReferences();
        StartPollingIfNeeded();
    }

    private void StartPollingIfNeeded()
    {
        if (Application.isPlaying &&
            pollingCoroutine == null &&
            settings != null &&
            settings.enableCommandPolling)
        {
            pollingCoroutine = StartCoroutine(PollLoop());
        }
    }

    private void OnDisable()
    {
        if (pollingCoroutine != null)
        {
            StopCoroutine(pollingCoroutine);
            pollingCoroutine = null;
        }

        if (commandStatus != null)
        {
            commandStatus.SetRequestInProgress(false);
        }
    }

    [ContextMenu("Resolve Command References")]
    public void ResolveReferences()
    {
        if (settings == null)
        {
            settings = GetComponent<P3BackendSettings>();
        }
        if (commandStatus == null)
        {
            commandStatus = GetComponent<P3CommandStatus>();
        }
        if (contractValidator == null)
        {
            contractValidator = GetComponent<P3CommandContractValidator>();
        }
        if (areaPolicyRuntime == null)
        {
            areaPolicyRuntime = GetComponent<P3AreaPolicyRuntime>();
        }
        if (areaPolicyRuntime == null)
        {
            areaPolicyRuntime = P3AreaPolicyRuntime.Instance;
        }
        if (eventPublisher == null)
        {
            eventPublisher = P2SimulationEventPublisher.Instance;
        }
#if UNITY_2023_1_OR_NEWER
        if (eventPublisher == null) eventPublisher = FindFirstObjectByType<P2SimulationEventPublisher>();
#else
        if (eventPublisher == null) eventPublisher = FindObjectOfType<P2SimulationEventPublisher>();
#endif
    }

    [ContextMenu("Poll Commands Now")]
    public void PollNow()
    {
        if (!Application.isPlaying || pollingCoroutine != null)
        {
            return;
        }

        pollingCoroutine = StartCoroutine(PollOnceThenStop());
    }

    private IEnumerator PollOnceThenStop()
    {
        yield return PollOnce();
        pollingCoroutine = null;
    }

    private IEnumerator PollLoop()
    {
        while (enabled)
        {
            ResolveReferences();
            if (settings == null || !settings.enableCommandPolling)
            {
                yield return new WaitForSecondsRealtime(1f);
                continue;
            }

            yield return PollOnce();
            float delay = consecutiveFailures == 0
                ? settings.EffectiveCommandPollingIntervalSeconds
                : CalculateBackoffDelay(consecutiveFailures);
            yield return new WaitForSecondsRealtime(delay);
        }

        pollingCoroutine = null;
    }

    private IEnumerator PollOnce()
    {
        ResolveReferences();

        if (settings == null || contractValidator == null || areaPolicyRuntime == null)
        {
            RegisterPollFailure(0, "P3 Command references are incomplete.");
            yield break;
        }

        string settingsError;
        if (!settings.TryValidateCommandSettings(out settingsError))
        {
            RegisterPollFailure(0, settingsError);
            yield break;
        }

        P2SimulationRunIdentity identity = P2SimulationRunContext.EnsureCurrentRun();
        EnsureIdempotencyCacheForRun(identity.runId);
        string url = BuildCommandUrl(identity);
        P3HttpResponse response = null;

        if (commandStatus != null)
        {
            commandStatus.SetRequestInProgress(true);
        }

        yield return StartCoroutine(P3HttpRequestUtility.Get(
            url,
            settings,
            settings.EffectiveCommandRequestTimeoutSeconds,
            value => response = value
        ));

        if (commandStatus != null)
        {
            commandStatus.SetRequestInProgress(false);
        }

        if (response == null)
        {
            RegisterPollFailure(0, "Command polling returned no response object.");
            yield break;
        }

        if (response.statusCode != 200)
        {
            RegisterPollFailure(response.statusCode, response.BuildDiagnosticMessage());
            yield break;
        }

        P3CommandBatch batch;
        try
        {
            batch = JsonUtility.FromJson<P3CommandBatch>(response.responseBody ?? string.Empty);
        }
        catch (Exception exception)
        {
            RegisterPollFailure(response.statusCode, "Command batch JSON parse failed: " + exception.Message);
            yield break;
        }

        P3CommandValidationResult batchValidation =
            contractValidator.ValidateBatch(batch, settings.EffectiveCommandMaximumCount);
        if (!batchValidation.isValid)
        {
            RegisterPollFailure(response.statusCode, "Command batch validation failed: " + batchValidation.BuildMessage());
            yield break;
        }

        consecutiveFailures = 0;
        if (commandStatus != null)
        {
            commandStatus.RecordPollSuccess(response.statusCode, settings.EffectiveCommandPollingIntervalSeconds);
        }

        if (logSuccessfulPolls)
        {
            Debug.Log("[P3Command] Poll succeeded. commands=" + batch.commands.Count);
        }

        foreach (P3Command command in batch.commands)
        {
            ExecuteCommand(command, identity);
        }
    }

    private void ExecuteCommand(P3Command command, P2SimulationRunIdentity identity)
    {
        if (commandStatus != null)
        {
            commandStatus.RecordReceived(command);
        }

        P3CommandValidationResult validation = contractValidator.ValidateCommand(command);
        if (!validation.isValid)
        {
            string reason = command != null &&
                            !string.Equals(command.commandType, P3CommandContractConstants.SetAreaPolicy, StringComparison.Ordinal)
                ? "UNKNOWN_COMMAND_TYPE"
                : "INVALID_PAYLOAD";
            Reject(command, reason, validation.BuildMessage());
            return;
        }

        if (!TargetMatches(command, identity))
        {
            Reject(command, "TARGET_MISMATCH", "Command target does not match the current Unity execution.");
            return;
        }

        DateTimeOffset expiresAtUtc;
        if (!P3CommandContractValidator.TryParseUtc(command.expiresAtUtc, out expiresAtUtc) ||
            DateTimeOffset.UtcNow >= expiresAtUtc)
        {
            MarkIdempotencyKeyProcessed(command.idempotencyKey);
            PublishTerminal(
                command,
                P2EventContractConstants.CommandExpired,
                "EXPIRED",
                "COMMAND_EXPIRED",
                "Command expired before execution started.",
                false,
                string.Empty,
                string.Empty,
                string.Empty
            );
            commandStatus?.RecordExpired(command);
            CacheTerminal(command, P2EventContractConstants.CommandExpired, "EXPIRED", "COMMAND_EXPIRED", "Command expired before execution started.", false, string.Empty, string.Empty, string.Empty);
            return;
        }

        CachedTerminalResult cached;
        if (processedIdempotencyKeys.Contains(command.idempotencyKey))
        {
            if (terminalResults.TryGetValue(command.idempotencyKey, out cached))
            {
                PublishTerminal(
                    command,
                    cached.eventType,
                    cached.status,
                    cached.reasonCode,
                    cached.message,
                    cached.retryable,
                    cached.areaId,
                    cached.previousPolicy,
                    cached.appliedPolicy
                );
            }
            return;
        }

        PublishProgress(command, P2EventContractConstants.CommandAccepted, "ACCEPTED");
        PublishProgress(command, P2EventContractConstants.CommandStarted, "STARTED");

        try
        {
            string previousPolicy;
            string appliedPolicy;
            string reasonCode;
            string message;
            bool applied = areaPolicyRuntime.TryApply(
                command,
                out previousPolicy,
                out appliedPolicy,
                out reasonCode,
                out message
            );

            if (!applied)
            {
                Reject(command, reasonCode, message);
                return;
            }

            string areaId = P3CommandContractValidator.NormalizeAreaId(command.payload.areaId);
            MarkIdempotencyKeyProcessed(command.idempotencyKey);
            PublishTerminal(
                command,
                P2EventContractConstants.CommandSucceeded,
                "SUCCEEDED",
                "POLICY_APPLIED",
                message,
                false,
                areaId,
                previousPolicy,
                appliedPolicy
            );
            CacheTerminal(command, P2EventContractConstants.CommandSucceeded, "SUCCEEDED", "POLICY_APPLIED", message, false, areaId, previousPolicy, appliedPolicy);
            commandStatus?.RecordSucceeded(command);

            if (logCommands)
            {
                Debug.Log("[P3Command] SET_AREA_POLICY succeeded. commandId=" + command.commandId);
            }
        }
        catch (Exception exception)
        {
            MarkIdempotencyKeyProcessed(command.idempotencyKey);
            string safeMessage = "SET_AREA_POLICY execution failed.";
            PublishTerminal(
                command,
                P2EventContractConstants.CommandFailed,
                "FAILED",
                "EXECUTION_ERROR",
                safeMessage,
                false,
                string.Empty,
                string.Empty,
                string.Empty
            );
            CacheTerminal(command, P2EventContractConstants.CommandFailed, "FAILED", "EXECUTION_ERROR", safeMessage, false, string.Empty, string.Empty, string.Empty);
            commandStatus?.RecordFailed(command, "EXECUTION_ERROR");

            if (logFailures)
            {
                Debug.LogError("[P3Command] SET_AREA_POLICY failed. " + exception);
            }
        }
    }

    private bool TargetMatches(P3Command command, P2SimulationRunIdentity identity)
    {
        return string.Equals(command.targetSourceId, settings.sourceId, StringComparison.Ordinal) &&
               string.Equals(command.targetSessionId, identity.sessionId, StringComparison.Ordinal) &&
               string.Equals(command.targetRunId, identity.runId, StringComparison.Ordinal);
    }

    private void Reject(P3Command command, string reasonCode, string message)
    {
        if (command != null && !string.IsNullOrWhiteSpace(command.idempotencyKey))
        {
            MarkIdempotencyKeyProcessed(command.idempotencyKey);
        }

        string eventType = P2EventContractConstants.CommandRejected;
        PublishTerminal(command, eventType, "REJECTED", reasonCode, SafeMessage(message), false, string.Empty, string.Empty, string.Empty);
        CacheTerminal(command, eventType, "REJECTED", reasonCode, SafeMessage(message), false, string.Empty, string.Empty, string.Empty);
        commandStatus?.RecordRejected(command, reasonCode);
    }

    private void PublishProgress(P3Command command, string eventType, string status)
    {
        if (eventPublisher == null || command == null)
        {
            return;
        }

        eventPublisher.PublishCommandResult(
            eventType,
            command.commandId,
            command.idempotencyKey,
            command.commandType,
            status,
            string.Empty,
            string.Empty,
            false,
            string.Empty,
            string.Empty,
            string.Empty
        );
    }

    private void PublishTerminal(
        P3Command command,
        string eventType,
        string status,
        string reasonCode,
        string message,
        bool retryable,
        string areaId,
        string previousPolicy,
        string appliedPolicy)
    {
        if (eventPublisher == null || command == null)
        {
            return;
        }

        eventPublisher.PublishCommandResult(
            eventType,
            command.commandId,
            command.idempotencyKey,
            command.commandType,
            status,
            reasonCode,
            SafeMessage(message),
            retryable,
            areaId,
            previousPolicy,
            appliedPolicy
        );
    }

    private void CacheTerminal(
        P3Command command,
        string eventType,
        string status,
        string reasonCode,
        string message,
        bool retryable,
        string areaId,
        string previousPolicy,
        string appliedPolicy)
    {
        if (command == null || string.IsNullOrWhiteSpace(command.idempotencyKey))
        {
            return;
        }

        terminalResults[command.idempotencyKey] = new CachedTerminalResult
        {
            eventType = eventType,
            status = status,
            reasonCode = reasonCode,
            message = SafeMessage(message),
            retryable = retryable,
            areaId = areaId,
            previousPolicy = previousPolicy,
            appliedPolicy = appliedPolicy
        };
    }

    private void EnsureIdempotencyCacheForRun(string runId)
    {
        string normalizedRunId = runId ?? string.Empty;
        if (string.Equals(
                idempotencyCacheRunId,
                normalizedRunId,
                StringComparison.Ordinal))
        {
            return;
        }

        ClearIdempotencyCache();
        idempotencyCacheRunId = normalizedRunId;
    }

    private void MarkIdempotencyKeyProcessed(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return;
        }

        if (processedIdempotencyKeys.Add(idempotencyKey))
        {
            idempotencyInsertionOrder.Enqueue(idempotencyKey);
        }

        TrimIdempotencyCache();
    }

    private void TrimIdempotencyCache()
    {
        int maximumEntries = Mathf.Max(1, maximumIdempotencyCacheEntries);
        while (processedIdempotencyKeys.Count > maximumEntries &&
               idempotencyInsertionOrder.Count > 0)
        {
            string oldestKey = idempotencyInsertionOrder.Dequeue();
            processedIdempotencyKeys.Remove(oldestKey);
            terminalResults.Remove(oldestKey);
        }
    }

    private void ClearIdempotencyCache()
    {
        processedIdempotencyKeys.Clear();
        terminalResults.Clear();
        idempotencyInsertionOrder.Clear();
    }

    private void OnValidate()
    {
        maximumIdempotencyCacheEntries = Mathf.Max(
            1,
            maximumIdempotencyCacheEntries
        );
    }

    private string BuildCommandUrl(P2SimulationRunIdentity identity)
    {
        string url = settings.BuildCommandUrl();
        string separator = url.Contains("?") ? "&" : "?";
        return url + separator +
               "sourceId=" + UnityWebRequest.EscapeURL(settings.sourceId ?? string.Empty) +
               "&sessionId=" + UnityWebRequest.EscapeURL(identity.sessionId ?? string.Empty) +
               "&runId=" + UnityWebRequest.EscapeURL(identity.runId ?? string.Empty) +
               "&limit=" + settings.EffectiveCommandMaximumCount;
    }

    private float CalculateBackoffDelay(int failures)
    {
        float initial = Mathf.Max(0.1f, settings.commandRetryInitialDelaySeconds);
        int exponent = Mathf.Max(0, failures - 1);
        float delay = initial * Mathf.Pow(2f, exponent);
        return Mathf.Min(delay, settings.commandRetryMaximumDelaySeconds);
    }

    private void RegisterPollFailure(long statusCode, string error)
    {
        consecutiveFailures++;
        float delay = settings != null
            ? CalculateBackoffDelay(consecutiveFailures)
            : 2f;
        commandStatus?.RecordPollFailure(statusCode, error, consecutiveFailures, delay);

        if (logFailures)
        {
            Debug.LogWarning("[P3Command] Poll failed. HTTP=" + statusCode + " retryIn=" + delay + "s " + (error ?? string.Empty));
        }
    }

    private static string SafeMessage(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        const int maxLength = 300;
        string safe = value.Replace("\r", " ").Replace("\n", " ");
        return safe.Length <= maxLength ? safe : safe.Substring(0, maxLength);
    }
}
