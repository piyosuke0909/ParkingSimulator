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
    [Header("Transmission")]
    public bool enableSnapshotTransmission = true;
    public bool enableEventTransmission = true;
    public bool enableHealthCheck = true;

    [Header("Backend Endpoints")]
    public string baseUrl = "http://localhost:8000";
    public string healthEndpoint = "/health";
    public string snapshotEndpoint = "/api/v1/snapshots";
    public string eventEndpoint = "/api/v1/events";

    [Header("Event Batch")]
    public P3EventBatchFormat eventBatchFormat = P3EventBatchFormat.Ndjson;

    [Min(1)]
    public int eventBatchSize = 50;

    [Min(0.1f)]
    public float eventFlushIntervalSeconds = 2f;

    [Header("HTTP")]
    [Min(1f)]
    public float requestTimeoutSeconds = 10f;

    public bool treatHttp409AsSuccess = true;

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
        eventBatchSize = Mathf.Max(1, eventBatchSize);
        eventFlushIntervalSeconds = Mathf.Max(0.1f, eventFlushIntervalSeconds);
        requestTimeoutSeconds = Mathf.Max(1f, requestTimeoutSeconds);
        retryDelaySeconds = Mathf.Max(0.1f, retryDelaySeconds);
        retryCooldownSeconds = Mathf.Max(1f, retryCooldownSeconds);
        maxRetryAttemptsBeforeCooldown = Mathf.Max(1, maxRetryAttemptsBeforeCooldown);
        healthCheckIntervalSeconds = Mathf.Max(1f, healthCheckIntervalSeconds);
    }
}
