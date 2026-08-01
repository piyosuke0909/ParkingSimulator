using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(-480)]
public class P3SnapshotHttpSender : MonoBehaviour
{
    [Header("Components")]
    public P3BackendSettings settings;
    public P3TransmissionStatus transmissionStatus;
    public P2LocalSnapshotWriter snapshotWriter;
    public P2SnapshotContractValidator contractValidator;

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
    private int pendingSnapshotCount;

    private bool immediateFlushRequested;

    public int PendingSnapshotCount => pendingSnapshotCount;
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
        if (snapshotWriter == null || !subscribed)
        {
            ResolveReferences();
            TrySubscribe();
        }

        if (!CanTransmit() || requestInProgress || pendingSnapshotCount <= 0)
        {
            return;
        }

        if (Time.unscaledTime < nextAttemptAtUnscaledTime)
        {
            return;
        }

        if (immediateFlushRequested || pendingSnapshotCount > 0)
        {
            immediateFlushRequested = false;
            StartCoroutine(SendNextSnapshot());
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
        requestInProgress = false;

        if (transmissionStatus != null)
        {
            transmissionStatus.SetSnapshotRequestInProgress(false);
        }
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    [ContextMenu("Resolve P3 Snapshot Sender References")]
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

        if (snapshotWriter == null)
        {
#if UNITY_2023_1_OR_NEWER
            snapshotWriter = FindFirstObjectByType<P2LocalSnapshotWriter>();
#else
            snapshotWriter = FindObjectOfType<P2LocalSnapshotWriter>();
#endif
        }

        if (contractValidator == null)
        {
#if UNITY_2023_1_OR_NEWER
            contractValidator = FindFirstObjectByType<P2SnapshotContractValidator>();
#else
            contractValidator = FindObjectOfType<P2SnapshotContractValidator>();
#endif
        }
    }

    [ContextMenu("Flush Pending Snapshots Now")]
    public void RequestImmediateFlush()
    {
        immediateFlushRequested = true;
        nextAttemptAtUnscaledTime = 0f;
        RefreshPendingCount();
    }

    [ContextMenu("Open Pending Snapshot Folder")]
    public void OpenPendingSnapshotFolder()
    {
        ResolveReferences();
        string path = settings != null
            ? settings.PendingSnapshotPath
            : Path.Combine(Application.persistentDataPath, "P3Pending", "Snapshots");
        Directory.CreateDirectory(path);
        Application.OpenURL("file:///" + path.Replace('\\', '/'));
    }

    private void TrySubscribe()
    {
        if (subscribed || snapshotWriter == null)
        {
            return;
        }

        snapshotWriter.SnapshotReady += HandleSnapshotReady;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
        {
            return;
        }

        if (snapshotWriter != null)
        {
            snapshotWriter.SnapshotReady -= HandleSnapshotReady;
        }

        subscribed = false;
    }

    private void HandleSnapshotReady(P2SimulationSnapshot snapshot, string json)
    {
        try
        {
            if (settings == null || !settings.enableSnapshotTransmission || snapshot == null)
            {
                return;
            }

            P2SnapshotValidationResult validation = contractValidator != null
                ? contractValidator.Validate(snapshot)
                : new P2SnapshotValidationResult { isValid = true };

            if (validation == null || !validation.isValid)
            {
                string reason = BuildValidationReason(validation);
                P3PendingFileStore.WriteFailedPayload(
                    settings.FailedSnapshotPath,
                    BuildSnapshotFileName(snapshot),
                    json,
                    reason
                );

                if (transmissionStatus != null)
                {
                    transmissionStatus.RecordSnapshotPermanentFailure(1, 0, reason);
                }

                if (settings.logPermanentFailures)
                {
                    Debug.LogError("[P3Snapshot] Contract validation failed. " + reason);
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                json = JsonUtility.ToJson(snapshot, false);
            }

            int requestBytes = Encoding.UTF8.GetByteCount(json);
            if (requestBytes > settings.EffectiveSnapshotMaximumRequestBytes)
            {
                string reason = BuildOversizedSnapshotReason(requestBytes);
                P3PendingFileStore.WriteFailedPayload(
                    settings.FailedSnapshotPath,
                    BuildSnapshotFileName(snapshot),
                    json,
                    reason
                );

                if (transmissionStatus != null)
                {
                    transmissionStatus.RecordSnapshotPermanentFailure(1, 0, reason);
                }

                if (settings.logPermanentFailures)
                {
                    Debug.LogError("[P3Snapshot] " + reason);
                }

                return;
            }

            EnsureQueueDirectories();
            P3PendingFileStore.WriteIfMissing(
                settings.PendingSnapshotPath,
                BuildSnapshotFileName(snapshot),
                json
            );

            RefreshPendingCount();
            immediateFlushRequested = true;
        }
        catch (Exception exception)
        {
            string message = "Failed to persist a Snapshot before transmission: " + exception;

            if (transmissionStatus != null)
            {
                transmissionStatus.RecordTransientFailure(0, message);
            }

            Debug.LogError("[P3Snapshot] " + message);
        }
    }

    private IEnumerator SendNextSnapshot()
    {
        if (!CanTransmit())
        {
            yield break;
        }

        string[] files = P3PendingFileStore.GetSortedJsonFiles(settings.PendingSnapshotPath);
        if (files.Length == 0)
        {
            RefreshPendingCount();
            yield break;
        }

        string path = files[0];
        string json;

        try
        {
            json = File.ReadAllText(path, Encoding.UTF8).Trim();

            if (string.IsNullOrWhiteSpace(json))
            {
                MovePendingFileToFailed(path, "Pending Snapshot file was empty.");
                RefreshPendingCount();
                yield break;
            }
        }
        catch (Exception exception)
        {
            MovePendingFileToFailed(
                path,
                "Pending Snapshot file could not be read: " + exception.Message
            );
            RefreshPendingCount();
            yield break;
        }

        int requestBytes = Encoding.UTF8.GetByteCount(json);
        if (requestBytes > settings.EffectiveSnapshotMaximumRequestBytes)
        {
            string reason = BuildOversizedSnapshotReason(requestBytes);
            MovePendingFileToFailed(path, reason);
            RefreshPendingCount();
            yield break;
        }

        requestInProgress = true;
        if (transmissionStatus != null)
        {
            transmissionStatus.SetSnapshotRequestInProgress(true);
        }

        P3HttpResponse response = null;
        yield return P3HttpRequestUtility.Post(
            settings.BuildSnapshotUrl(),
            "application/json",
            json,
            settings,
            value => response = value
        );

        requestInProgress = false;
        if (transmissionStatus != null)
        {
            transmissionStatus.SetSnapshotRequestInProgress(false);
        }

        if (response != null && response.IsAccepted(settings))
        {
            P3PendingFileStore.DeleteFiles(new[] { path });
            consecutiveRetryAttempts = 0;
            nextAttemptAtUnscaledTime = 0f;

            if (transmissionStatus != null)
            {
                transmissionStatus.RecordSnapshotSuccess(1, response.statusCode);
            }

            if (settings.logSuccessfulRequests)
            {
                Debug.Log(
                    "[P3Snapshot] Sent Snapshot. HTTP " +
                    response.statusCode.ToString(CultureInfo.InvariantCulture)
                );
            }
        }
        else if (response != null && !response.IsTransientFailure())
        {
            string reason = BuildResponseFailureMessage(response);
            P3PendingFileStore.MoveToFailed(path, settings.FailedSnapshotPath, reason);
            consecutiveRetryAttempts = 0;
            nextAttemptAtUnscaledTime = 0f;

            if (transmissionStatus != null)
            {
                transmissionStatus.RecordSnapshotPermanentFailure(1, response.statusCode, reason);
            }

            if (settings.logPermanentFailures)
            {
                Debug.LogError("[P3Snapshot] Permanent HTTP failure. " + reason);
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
                "[P3Snapshot] Transmission failed and the pending file was retained. " +
                "Retry after " + delay.ToString("F1", CultureInfo.InvariantCulture) +
                " real seconds. " + reason
            );
        }
    }

    private void MovePendingFileToFailed(string path, string reason)
    {
        try
        {
            P3PendingFileStore.MoveToFailed(path, settings.FailedSnapshotPath, reason);

            if (transmissionStatus != null)
            {
                transmissionStatus.RecordSnapshotPermanentFailure(1, 0, reason);
            }
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[P3Snapshot] Failed to quarantine a pending file: " + exception
            );
        }
    }

    private void RefreshPendingCount()
    {
        pendingSnapshotCount = settings != null
            ? P3PendingFileStore.CountJsonFiles(settings.PendingSnapshotPath)
            : 0;

        if (transmissionStatus != null)
        {
            transmissionStatus.SetPendingSnapshotCount(pendingSnapshotCount);
        }
    }

    private void EnsureQueueDirectories()
    {
        if (settings == null)
        {
            return;
        }

        Directory.CreateDirectory(settings.PendingSnapshotPath);
        Directory.CreateDirectory(settings.FailedSnapshotPath);
    }

    private bool CanTransmit()
    {
        if (settings == null || !settings.enableSnapshotTransmission)
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

    private string BuildOversizedSnapshotReason(int actualBytes)
    {
        return "Snapshot request body exceeds the confirmed Backend limit. Actual=" +
               actualBytes.ToString(CultureInfo.InvariantCulture) +
               " bytes, Limit=" +
               settings.EffectiveSnapshotMaximumRequestBytes.ToString(CultureInfo.InvariantCulture) +
               " bytes.";
    }

    private static string BuildSnapshotFileName(P2SimulationSnapshot snapshot)
    {
        string runId = snapshot != null ? snapshot.runId : "run";
        long sequence = snapshot != null ? snapshot.sequenceNumber : 0L;

        return "snapshot-" +
               P3PendingFileStore.SanitizePathPart(runId, "run") + "-seq" +
               sequence.ToString("D12", CultureInfo.InvariantCulture) + ".json";
    }

    private static string BuildValidationReason(P2SnapshotValidationResult validation)
    {
        if (validation == null || validation.errors == null || validation.errors.Count == 0)
        {
            return "P2 Snapshot contract validation failed.";
        }

        return "P2 Snapshot contract validation failed: " +
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
