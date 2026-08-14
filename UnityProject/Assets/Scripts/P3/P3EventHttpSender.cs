using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(-485)]
public class P3EventHttpSender : MonoBehaviour
{
    [Header("Components")]
    public P3BackendSettings settings;
    public P3TransmissionStatus transmissionStatus;
    public P2SimulationEventPublisher eventPublisher;
    public P2EventContractValidator contractValidator;

    [Header("Runtime (Read Only)")]
    [SerializeField]
    private bool subscribed;

    [SerializeField]
    private bool requestInProgress;

    [SerializeField]
    private int consecutiveRetryAttempts;

    [SerializeField]
    private float nextAttemptAtUnscaledTime;

    [SerializeField]
    private int pendingEventCount;

    private bool immediateFlushRequested;
    private float nextRegularFlushAtUnscaledTime;

    public int PendingEventCount => pendingEventCount;
    public bool RequestInProgress => requestInProgress;

    private void Awake()
    {
        ResolveReferences();
        EnsureQueueDirectories();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureQueueDirectories();
        TrySubscribe();
        ResetFlushSchedule();
        RefreshPendingCount();
    }

    private void Start()
    {
        ResolveReferences();
        TrySubscribe();
        RefreshPendingCount();
    }

    private void Update()
    {
        if (eventPublisher == null || !subscribed)
        {
            ResolveReferences();
            TrySubscribe();
        }

        if (!CanTransmit() || requestInProgress)
        {
            return;
        }

        if (Time.unscaledTime < nextAttemptAtUnscaledTime)
        {
            return;
        }

        if (pendingEventCount <= 0)
        {
            if (Time.unscaledTime >= nextRegularFlushAtUnscaledTime)
            {
                RefreshPendingCount();
                ResetFlushSchedule();
            }

            return;
        }

        bool batchFull = pendingEventCount >= settings.EffectiveEventBatchSize;
        bool intervalElapsed = Time.unscaledTime >= nextRegularFlushAtUnscaledTime;

        if (immediateFlushRequested || batchFull || intervalElapsed)
        {
            immediateFlushRequested = false;
            StartCoroutine(SendNextBatch());
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
        requestInProgress = false;

        if (transmissionStatus != null)
        {
            transmissionStatus.SetEventRequestInProgress(false);
        }
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    [ContextMenu("Resolve P3 Event Sender References")]
    public void ResolveReferences()
    {
        if (settings == null)
        {
            settings = GetComponent<P3BackendSettings>();
        }

        if (transmissionStatus == null)
        {
            transmissionStatus = GetComponent<P3TransmissionStatus>();
        }

        if (eventPublisher == null)
        {
            eventPublisher = P2SimulationEventPublisher.Instance;
        }

        if (eventPublisher == null)
        {
#if UNITY_2023_1_OR_NEWER
            eventPublisher = FindFirstObjectByType<P2SimulationEventPublisher>();
#else
            eventPublisher = FindObjectOfType<P2SimulationEventPublisher>();
#endif
        }

        if (contractValidator == null)
        {
#if UNITY_2023_1_OR_NEWER
            contractValidator = FindFirstObjectByType<P2EventContractValidator>();
#else
            contractValidator = FindObjectOfType<P2EventContractValidator>();
#endif
        }
    }

    [ContextMenu("Flush Pending Events Now")]
    public void RequestImmediateFlush()
    {
        immediateFlushRequested = true;
        nextAttemptAtUnscaledTime = 0f;
        RefreshPendingCount();
    }

    [ContextMenu("Open Pending Event Folder")]
    public void OpenPendingEventFolder()
    {
        ResolveReferences();
        string path = settings != null
            ? settings.PendingEventPath
            : Path.Combine(Application.persistentDataPath, "P3Pending", "Events");
        Directory.CreateDirectory(path);
        Application.OpenURL("file:///" + path.Replace('\\', '/'));
    }

    private void TrySubscribe()
    {
        if (subscribed || eventPublisher == null)
        {
            return;
        }

        eventPublisher.EventPublished += HandleEventPublished;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
        {
            return;
        }

        if (eventPublisher != null)
        {
            eventPublisher.EventPublished -= HandleEventPublished;
        }

        subscribed = false;
    }

    private void HandleEventPublished(P2SimulationEvent simulationEvent)
    {
        try
        {
            if (settings == null || !settings.enableEventTransmission || simulationEvent == null)
            {
                return;
            }

            P2EventValidationResult validation = contractValidator != null
                ? contractValidator.Validate(simulationEvent)
                : new P2EventValidationResult { isValid = true };

            string json = P2EventJsonSerializer.ToJson(simulationEvent);

            if (validation == null || !validation.isValid)
            {
                string reason = BuildValidationReason(validation);
                string failedName = BuildEventFileName(simulationEvent);
                P3PendingFileStore.WriteFailedPayload(
                    settings.FailedEventPath,
                    failedName,
                    json,
                    reason
                );

                if (transmissionStatus != null)
                {
                    transmissionStatus.RecordEventPermanentFailure(1, 0, reason);
                }

                if (settings.logPermanentFailures)
                {
                    Debug.LogError("[P3Event] Contract validation failed. " + reason);
                }

                return;
            }

            int requestBytes = GetBatchBodyByteCount(new List<string> { json });
            if (requestBytes > settings.EffectiveEventMaximumRequestBytes)
            {
                string reason = BuildOversizedEventReason(requestBytes);
                P3PendingFileStore.WriteFailedPayload(
                    settings.FailedEventPath,
                    BuildEventFileName(simulationEvent),
                    json,
                    reason
                );

                if (transmissionStatus != null)
                {
                    transmissionStatus.RecordEventPermanentFailure(1, 0, reason);
                }

                if (settings.logPermanentFailures)
                {
                    Debug.LogError("[P3Event] " + reason);
                }

                return;
            }

            EnsureQueueDirectories();
            P3PendingFileStore.WriteIfMissing(
                settings.PendingEventPath,
                BuildEventFileName(simulationEvent),
                json
            );

            RefreshPendingCount();

            if (pendingEventCount >= settings.EffectiveEventBatchSize)
            {
                immediateFlushRequested = true;
            }
        }
        catch (Exception exception)
        {
            string message = "Failed to persist an Event before transmission: " + exception;

            if (transmissionStatus != null)
            {
                transmissionStatus.RecordTransientFailure(0, message);
            }

            Debug.LogError("[P3Event] " + message);
        }
    }

    private IEnumerator SendNextBatch()
    {
        if (!CanTransmit())
        {
            yield break;
        }

        string[] allFiles = P3PendingFileStore.GetSortedJsonFiles(settings.PendingEventPath);
        if (allFiles.Length == 0)
        {
            RefreshPendingCount();
            ResetFlushSchedule();
            yield break;
        }

        int maximum = Mathf.Min(settings.EffectiveEventBatchSize, allFiles.Length);
        int maximumBytes = settings.EffectiveEventMaximumRequestBytes;
        List<string> batchPaths = new List<string>(maximum);
        List<string> eventJsonList = new List<string>(maximum);

        for (int i = 0; i < allFiles.Length && batchPaths.Count < maximum; i++)
        {
            string path = allFiles[i];

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8).Trim();
                if (string.IsNullOrWhiteSpace(json))
                {
                    MovePendingFileToFailed(path, "Pending Event file was empty.");
                    continue;
                }

                int singleEventBytes = GetBatchBodyByteCount(new List<string> { json });
                if (singleEventBytes > maximumBytes)
                {
                    MovePendingFileToFailed(path, BuildOversizedEventReason(singleEventBytes));
                    continue;
                }

                eventJsonList.Add(json);
                int candidateBytes = GetBatchBodyByteCount(eventJsonList);
                if (candidateBytes > maximumBytes)
                {
                    eventJsonList.RemoveAt(eventJsonList.Count - 1);
                    break;
                }

                batchPaths.Add(path);
            }
            catch (Exception exception)
            {
                MovePendingFileToFailed(
                    path,
                    "Pending Event file could not be read: " + exception.Message
                );
            }
        }

        if (batchPaths.Count == 0)
        {
            RefreshPendingCount();
            ResetFlushSchedule();
            yield break;
        }

        string requestBody = BuildBatchBody(eventJsonList);
        string contentType = settings.eventBatchFormat == P3EventBatchFormat.Ndjson
            ? "application/x-ndjson"
            : "application/json";

        requestInProgress = true;
        if (transmissionStatus != null)
        {
            transmissionStatus.SetEventRequestInProgress(true);
        }

        P3HttpResponse response = null;
        yield return P3HttpRequestUtility.Post(
            settings.BuildEventUrl(),
            contentType,
            requestBody,
            settings,
            value => response = value
        );

        requestInProgress = false;
        if (transmissionStatus != null)
        {
            transmissionStatus.SetEventRequestInProgress(false);
        }

        if (response != null && response.IsAccepted(settings))
        {
            P3PendingFileStore.DeleteFiles(batchPaths);
            consecutiveRetryAttempts = 0;
            nextAttemptAtUnscaledTime = 0f;

            if (transmissionStatus != null)
            {
                transmissionStatus.RecordEventSuccess(batchPaths.Count, response.statusCode);
            }

            if (settings.logSuccessfulRequests)
            {
                Debug.Log(
                    "[P3Event] Sent " + batchPaths.Count.ToString(CultureInfo.InvariantCulture) +
                    " Event(s). HTTP " + response.statusCode.ToString(CultureInfo.InvariantCulture)
                );
            }
        }
        else if (response != null && !response.IsTransientFailure())
        {
            string reason = BuildResponseFailureMessage(response);

            foreach (string path in batchPaths)
            {
                P3PendingFileStore.MoveToFailed(path, settings.FailedEventPath, reason);
            }

            consecutiveRetryAttempts = 0;
            nextAttemptAtUnscaledTime = 0f;

            if (transmissionStatus != null)
            {
                transmissionStatus.RecordEventPermanentFailure(
                    batchPaths.Count,
                    response.statusCode,
                    reason
                );
            }

            if (settings.logPermanentFailures)
            {
                Debug.LogError("[P3Event] Permanent HTTP failure. " + reason);
            }
        }
        else
        {
            string reason = response != null
                ? BuildResponseFailureMessage(response)
                : "HTTP response was not returned.";
            long statusCode = response != null ? response.statusCode : 0;

            ScheduleRetry(statusCode, reason);
        }

        RefreshPendingCount();
        ResetFlushSchedule();
    }

    private string BuildBatchBody(List<string> events)
    {
        if (settings.eventBatchFormat == P3EventBatchFormat.JsonEnvelope)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("{\"events\":[");

            for (int i = 0; i < events.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(events[i]);
            }

            builder.Append("]}");
            return builder.ToString();
        }

        return string.Join("\n", events.ToArray()) + "\n";
    }

    private int GetBatchBodyByteCount(List<string> events)
    {
        return Encoding.UTF8.GetByteCount(BuildBatchBody(events));
    }

    private string BuildOversizedEventReason(int actualBytes)
    {
        return "Event request body exceeds the confirmed Backend limit. Actual=" +
               actualBytes.ToString(CultureInfo.InvariantCulture) +
               " bytes, Limit=" +
               settings.EffectiveEventMaximumRequestBytes.ToString(CultureInfo.InvariantCulture) +
               " bytes.";
    }

    private void ScheduleRetry(long statusCode, string reason)
    {
        consecutiveRetryAttempts++;
        int maximumAttempts = Mathf.Max(1, settings.maxRetryAttemptsBeforeCooldown);
        float delay;

        if (consecutiveRetryAttempts >= maximumAttempts)
        {
            delay = Mathf.Max(1f, settings.retryCooldownSeconds);
            consecutiveRetryAttempts = 0;
        }
        else if (settings.useExponentialBackoff)
        {
            int exponent = Mathf.Clamp(consecutiveRetryAttempts - 1, 0, 6);
            delay = Mathf.Max(0.1f, settings.retryDelaySeconds) * Mathf.Pow(2f, exponent);
        }
        else
        {
            delay = Mathf.Max(0.1f, settings.retryDelaySeconds);
        }

        nextAttemptAtUnscaledTime = Time.unscaledTime + delay;

        if (transmissionStatus != null)
        {
            transmissionStatus.RecordTransientFailure(statusCode, reason);
        }

        if (settings.logRetryWarnings)
        {
            Debug.LogWarning(
                "[P3Event] Transmission failed and pending files were retained. " +
                "Retry after " + delay.ToString("F1", CultureInfo.InvariantCulture) +
                " real seconds. " + reason
            );
        }
    }

    private void MovePendingFileToFailed(string path, string reason)
    {
        try
        {
            P3PendingFileStore.MoveToFailed(path, settings.FailedEventPath, reason);

            if (transmissionStatus != null)
            {
                transmissionStatus.RecordEventPermanentFailure(1, 0, reason);
            }
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[P3Event] Failed to quarantine a pending file: " + exception
            );
        }
    }

    private void RefreshPendingCount()
    {
        pendingEventCount = settings != null
            ? P3PendingFileStore.CountJsonFiles(settings.PendingEventPath)
            : 0;

        if (transmissionStatus != null)
        {
            transmissionStatus.SetPendingEventCount(pendingEventCount);
        }
    }

    private void ResetFlushSchedule()
    {
        float interval = settings != null
            ? Mathf.Max(0.1f, settings.eventFlushIntervalSeconds)
            : 2f;
        nextRegularFlushAtUnscaledTime = Time.unscaledTime + interval;
    }

    private void EnsureQueueDirectories()
    {
        if (settings == null)
        {
            return;
        }

        Directory.CreateDirectory(settings.PendingEventPath);
        Directory.CreateDirectory(settings.FailedEventPath);
    }

    private bool CanTransmit()
    {
        if (settings == null || !settings.enableEventTransmission)
        {
            return false;
        }

        string error;
        if (!settings.TryValidate(out error))
        {
            if (transmissionStatus != null)
            {
                transmissionStatus.RecordTransientFailure(0, error);
            }
            return false;
        }

        return true;
    }

    private static string BuildEventFileName(P2SimulationEvent simulationEvent)
    {
        string runId = simulationEvent != null ? simulationEvent.runId : "run";
        long sequence = simulationEvent != null ? simulationEvent.sequenceNumber : 0L;

        return "event-" +
               P3PendingFileStore.SanitizePathPart(runId, "run") + "-seq" +
               sequence.ToString("D12", CultureInfo.InvariantCulture) + ".json";
    }

    private static string BuildValidationReason(P2EventValidationResult validation)
    {
        if (validation == null || validation.errors == null || validation.errors.Count == 0)
        {
            return "P2 Event contract validation failed.";
        }

        return "P2 Event contract validation failed: " +
               string.Join(" | ", validation.errors.ToArray());
    }

    private static string BuildResponseFailureMessage(P3HttpResponse response)
    {
        if (response == null)
        {
            return "HTTP response was not returned.";
        }

        return "HTTP " + response.statusCode.ToString(CultureInfo.InvariantCulture) +
               ": " + response.BuildDiagnosticMessage();
    }
}
