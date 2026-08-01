using System;
using System.IO;
using UnityEngine;

public enum P3AuthenticationMode
{
    None,
    ApiKeyHeader,
    BearerToken
}

public enum P3EventBatchFormat
{
    Ndjson,
    JsonEnvelope
}

public class P3BackendSettings : MonoBehaviour
{
    public const int BackendMaximumEventBatchCount = 50;
    public const int BackendMaximumEventRequestBytes = 1024 * 1024;
    public const int BackendMaximumSnapshotRequestBytes = 5 * 1024 * 1024;

    [Header("Transmission")]
    public bool enableSnapshotTransmission = true;
    public bool enableEventTransmission = true;
    public bool enableHealthCheck = true;

    [Header("Backend Endpoints")]
    public string baseUrl = "http://localhost:8000";
    public string healthEndpoint = "/api/health";
    public string snapshotEndpoint = "/api/v1/snapshots";
    public string eventEndpoint = "/api/v1/events";

    [Header("Event Batch")]
    public P3EventBatchFormat eventBatchFormat = P3EventBatchFormat.Ndjson;

    [Range(1, BackendMaximumEventBatchCount)]
    public int eventBatchSize = BackendMaximumEventBatchCount;

    [Min(0.1f)]
    public float eventFlushIntervalSeconds = 2f;

    [Header("Backend Request Size Limits (UTF-8 bytes)")]
    [Tooltip("Backend limit: 1 MiB per Event NDJSON request.")]
    [Min(1)]
    public int eventMaximumRequestBytes = BackendMaximumEventRequestBytes;

    [Tooltip("Backend limit: 5 MiB per Snapshot JSON request.")]
    [Min(1)]
    public int snapshotMaximumRequestBytes = BackendMaximumSnapshotRequestBytes;

    [Header("HTTP")]
    [Min(1f)]
    public float requestTimeoutSeconds = 10f;

    [Tooltip("Confirmed Backend contract uses HTTP 409 for conflicting payloads. Keep this OFF.")]
    public bool treatHttp409AsSuccess = false;

    [Header("Retry")]
    [Min(0.1f)]
    public float retryDelaySeconds = 2f;

    [Min(1f)]
    public float retryCooldownSeconds = 30f;

    [Min(1)]
    public int maxRetryAttemptsBeforeCooldown = 5;

    public bool useExponentialBackoff = true;

    [Header("Health Check")]
    [Min(1f)]
    public float healthCheckIntervalSeconds = 10f;

    [Header("Authentication")]
    public P3AuthenticationMode authenticationMode = P3AuthenticationMode.None;
    public string apiKeyHeaderName = "X-API-Key";
    public string apiKey = string.Empty;
    public string bearerToken = string.Empty;

    [Header("Persistent Queue")]
    public string pendingDirectoryName = "P3Pending";
    public string failedDirectoryName = "P3Failed";

    [Header("Logging")]
    public bool logSuccessfulRequests = false;
    public bool logRetryWarnings = true;
    public bool logPermanentFailures = true;

    public int EffectiveEventBatchSize => Mathf.Clamp(
        eventBatchSize,
        1,
        BackendMaximumEventBatchCount
    );

    public int EffectiveEventMaximumRequestBytes => eventMaximumRequestBytes <= 0
        ? BackendMaximumEventRequestBytes
        : Mathf.Min(eventMaximumRequestBytes, BackendMaximumEventRequestBytes);

    public int EffectiveSnapshotMaximumRequestBytes => snapshotMaximumRequestBytes <= 0
        ? BackendMaximumSnapshotRequestBytes
        : Mathf.Min(snapshotMaximumRequestBytes, BackendMaximumSnapshotRequestBytes);

    public string PendingRootPath => Path.Combine(
        Application.persistentDataPath,
        P3PendingFileStore.SanitizePathPart(pendingDirectoryName, "P3Pending")
    );

    public string FailedRootPath => Path.Combine(
        Application.persistentDataPath,
        P3PendingFileStore.SanitizePathPart(failedDirectoryName, "P3Failed")
    );

    public string PendingEventPath => Path.Combine(PendingRootPath, "Events");
    public string PendingSnapshotPath => Path.Combine(PendingRootPath, "Snapshots");
    public string FailedEventPath => Path.Combine(FailedRootPath, "Events");
    public string FailedSnapshotPath => Path.Combine(FailedRootPath, "Snapshots");

    public string BuildHealthUrl()
    {
        return BuildUrl(healthEndpoint);
    }

    public string BuildSnapshotUrl()
    {
        return BuildUrl(snapshotEndpoint);
    }

    public string BuildEventUrl()
    {
        return BuildUrl(eventEndpoint);
    }

    public string BuildUrl(string endpoint)
    {
        string root = string.IsNullOrWhiteSpace(baseUrl)
            ? string.Empty
            : baseUrl.Trim().TrimEnd('/');
        string path = string.IsNullOrWhiteSpace(endpoint)
            ? string.Empty
            : endpoint.Trim();

        if (string.IsNullOrWhiteSpace(root))
        {
            return path;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return root;
        }

        return root + "/" + path.TrimStart('/');
    }

    public bool TryValidate(out string error)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            error = "Backend Base URL is empty.";
            return false;
        }

        Uri parsed;
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            error = "Backend Base URL must be an absolute HTTP or HTTPS URL.";
            return false;
        }

        if (enableSnapshotTransmission && string.IsNullOrWhiteSpace(snapshotEndpoint))
        {
            error = "Snapshot Endpoint is empty.";
            return false;
        }

        if (enableEventTransmission && string.IsNullOrWhiteSpace(eventEndpoint))
        {
            error = "Event Endpoint is empty.";
            return false;
        }

        if (enableHealthCheck && string.IsNullOrWhiteSpace(healthEndpoint))
        {
            error = "Health Endpoint is empty.";
            return false;
        }

        if (enableEventTransmission && eventBatchFormat != P3EventBatchFormat.Ndjson)
        {
            error = "The confirmed Backend contract requires application/x-ndjson for Events.";
            return false;
        }

        if (eventBatchSize < 1 || eventBatchSize > BackendMaximumEventBatchCount)
        {
            error = "Event Batch Size must be between 1 and 50.";
            return false;
        }

        if (treatHttp409AsSuccess)
        {
            error = "Treat Http 409 As Success must be OFF. HTTP 409 means an ID payload conflict.";
            return false;
        }

        if (authenticationMode == P3AuthenticationMode.ApiKeyHeader &&
            string.IsNullOrWhiteSpace(apiKeyHeaderName))
        {
            error = "API key header name is empty.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void OnValidate()
    {
        eventBatchSize = Mathf.Clamp(eventBatchSize, 1, BackendMaximumEventBatchCount);
        eventFlushIntervalSeconds = Mathf.Max(0.1f, eventFlushIntervalSeconds);
        eventMaximumRequestBytes = Mathf.Clamp(
            eventMaximumRequestBytes <= 0
                ? BackendMaximumEventRequestBytes
                : eventMaximumRequestBytes,
            1,
            BackendMaximumEventRequestBytes
        );
        snapshotMaximumRequestBytes = Mathf.Clamp(
            snapshotMaximumRequestBytes <= 0
                ? BackendMaximumSnapshotRequestBytes
                : snapshotMaximumRequestBytes,
            1,
            BackendMaximumSnapshotRequestBytes
        );
        requestTimeoutSeconds = Mathf.Max(1f, requestTimeoutSeconds);
        retryDelaySeconds = Mathf.Max(0.1f, retryDelaySeconds);
        retryCooldownSeconds = Mathf.Max(1f, retryCooldownSeconds);
        maxRetryAttemptsBeforeCooldown = Mathf.Max(1, maxRetryAttemptsBeforeCooldown);
        healthCheckIntervalSeconds = Mathf.Max(1f, healthCheckIntervalSeconds);
    }
}
