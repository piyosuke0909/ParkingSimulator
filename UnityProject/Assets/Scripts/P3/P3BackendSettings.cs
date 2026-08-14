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
    public const int BackendMaximumCommandCount = 10;

    [Header("Transmission")]
    public bool enableSnapshotTransmission = true;
    public bool enableEventTransmission = true;
    public bool enableHealthCheck = true;
    public bool enableCommandPolling = true;

    [Header("Backend Endpoints")]
    public string baseUrl = "http://localhost:8000";
    public string healthEndpoint = "/api/health";
    public string snapshotEndpoint = "/api/v1/snapshots";
    public string eventEndpoint = "/api/v1/events";
    public string commandEndpoint = "/api/v1/commands";

    [Header("Unity Identity")]
    public string sourceId = "unity-webgl-admin-01";

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

    [Header("Command Polling v1.0")]
    [Min(0.1f)]
    public float commandPollingIntervalSeconds = 1f;

    [Min(1f)]
    public float commandRequestTimeoutSeconds = 5f;

    [Range(1, BackendMaximumCommandCount)]
    public int commandMaximumCount = BackendMaximumCommandCount;

    [Min(0.1f)]
    public float commandRetryInitialDelaySeconds = 2f;

    [Min(1f)]
    public float commandRetryMaximumDelaySeconds = 30f;

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
    public P3AuthenticationMode authenticationMode = P3AuthenticationMode.ApiKeyHeader;
    public string apiKeyHeaderName = "X-API-Key";

    [Tooltip("Name of the OS environment/.env variable containing the API key. The secret value itself is never serialized into the Scene.")]
    public string apiKeyEnvironmentVariable = "SMARTPARKING_LOCAL_API_KEY";

    [Tooltip("Name of the OS environment/.env variable containing the bearer token. The secret value itself is never serialized into the Scene.")]
    public string bearerTokenEnvironmentVariable = "SMARTPARKING_BEARER_TOKEN";

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

    public float EffectiveCommandPollingIntervalSeconds => Mathf.Max(0.1f, commandPollingIntervalSeconds);
    public float EffectiveCommandRequestTimeoutSeconds => Mathf.Max(1f, commandRequestTimeoutSeconds);
    public int EffectiveCommandMaximumCount => Mathf.Clamp(commandMaximumCount, 1, BackendMaximumCommandCount);

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

    public string BuildCommandUrl()
    {
        return BuildUrl(commandEndpoint);
    }

    public bool TryResolveApiKey(out string value)
    {
        return P3SecretProvider.TryResolveApiKey(apiKeyEnvironmentVariable, out value);
    }

    public bool TryResolveBearerToken(out string value)
    {
        return P3SecretProvider.TryResolveBearerToken(
            bearerTokenEnvironmentVariable,
            out value
        );
    }

    public void SetRuntimeApiKey(string value)
    {
        P3SecretProvider.SetRuntimeApiKey(value);
    }

    public void SetRuntimeBearerToken(string value)
    {
        P3SecretProvider.SetRuntimeBearerToken(value);
    }

    public void ClearRuntimeAuthenticationOverrides()
    {
        P3SecretProvider.ClearRuntimeOverrides();
    }

    [ContextMenu("Reload Authentication Secrets")]
    public void ReloadAuthenticationSecrets()
    {
        P3SecretProvider.ReloadDotEnv();
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

        if (enableCommandPolling && string.IsNullOrWhiteSpace(commandEndpoint))
        {
            error = "Command Endpoint is empty.";
            return false;
        }

        if (enableCommandPolling && string.IsNullOrWhiteSpace(sourceId))
        {
            error = "Unity sourceId is empty.";
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

        if (authenticationMode == P3AuthenticationMode.ApiKeyHeader &&
            string.IsNullOrWhiteSpace(apiKeyEnvironmentVariable))
        {
            error = "API key environment variable name is empty.";
            return false;
        }

        if (authenticationMode == P3AuthenticationMode.BearerToken &&
            string.IsNullOrWhiteSpace(bearerTokenEnvironmentVariable))
        {
            error = "Bearer token environment variable name is empty.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public bool TryValidateCommandSettings(out string error)
    {
        if (!TryValidate(out error))
        {
            return false;
        }

        if (!enableCommandPolling)
        {
            error = string.Empty;
            return true;
        }

        if (commandMaximumCount < 1 || commandMaximumCount > BackendMaximumCommandCount)
        {
            error = "Command maximum count must be between 1 and 10.";
            return false;
        }

        if (commandRetryMaximumDelaySeconds < commandRetryInitialDelaySeconds)
        {
            error = "Command maximum retry delay must be greater than or equal to the initial delay.";
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
        commandPollingIntervalSeconds = Mathf.Max(0.1f, commandPollingIntervalSeconds);
        commandRequestTimeoutSeconds = Mathf.Max(1f, commandRequestTimeoutSeconds);
        commandMaximumCount = Mathf.Clamp(commandMaximumCount, 1, BackendMaximumCommandCount);
        commandRetryInitialDelaySeconds = Mathf.Max(0.1f, commandRetryInitialDelaySeconds);
        commandRetryMaximumDelaySeconds = Mathf.Max(commandRetryInitialDelaySeconds, commandRetryMaximumDelaySeconds);
        retryDelaySeconds = Mathf.Max(0.1f, retryDelaySeconds);
        retryCooldownSeconds = Mathf.Max(1f, retryCooldownSeconds);
        maxRetryAttemptsBeforeCooldown = Mathf.Max(1, maxRetryAttemptsBeforeCooldown);
        healthCheckIntervalSeconds = Mathf.Max(1f, healthCheckIntervalSeconds);
    }
}
