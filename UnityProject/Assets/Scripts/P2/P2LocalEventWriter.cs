using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(-490)]
public class P2LocalEventWriter : MonoBehaviour
{
    [Header("Components")]
    public P2SimulationEventPublisher eventPublisher;
    public P2EventContractValidator contractValidator;

    [Header("File Output")]
    public bool enableFileOutput = true;
    public string outputDirectoryName = "P2Events";
    public string fileNamePrefix = "events";
    public bool flushAfterEveryEvent = true;
    public bool writeWhenValidationFails = false;

    [Header("Logging")]
    public bool logSuccess = false;
    public bool logValidationWarnings = true;
    public bool logErrors = true;

    [Header("Runtime (Read Only)")]
    [SerializeField]
    private string currentFilePath;

    [SerializeField]
    private long writtenEventCount;

    [SerializeField]
    private bool fileLoggingDisabledAfterFailure;

    private StreamWriter writer;
    private string openedRunId;
    private bool subscribed;

    public string CurrentFilePath => currentFilePath;
    public long WrittenEventCount => writtenEventCount;
    public bool FileLoggingDisabledAfterFailure => fileLoggingDisabledAfterFailure;
    public string OutputDirectoryPath => Path.Combine(
        Application.persistentDataPath,
        SanitizePathPart(outputDirectoryName, "P2Events")
    );

    private void Awake()
    {
        ResolveReferences();
        TrySubscribe();
    }

    private void OnEnable()
    {
        ResolveReferences();
        TrySubscribe();
    }

    private void Start()
    {
        ResolveReferences();
        TrySubscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
        CloseWriter();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        CloseWriter();
    }

    private void OnApplicationQuit()
    {
        CloseWriter();
    }

    [ContextMenu("Resolve Event Writer References")]
    public void ResolveReferences()
    {
        if (eventPublisher == null)
        {
            eventPublisher = GetComponent<P2SimulationEventPublisher>();
        }

        if (contractValidator == null)
        {
            contractValidator = GetComponent<P2EventContractValidator>();
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

    [ContextMenu("Open Event Output Folder")]
    public void OpenEventOutputFolder()
    {
        string path = OutputDirectoryPath;
        Directory.CreateDirectory(path);
        Application.OpenURL("file:///" + path.Replace('\\', '/'));
    }

    [ContextMenu("Retry Event File Logging")]
    public void RetryFileLogging()
    {
        CloseWriter();
        fileLoggingDisabledAfterFailure = false;
        openedRunId = string.Empty;

        if (logErrors)
        {
            Debug.Log("[P2Event] File logging retry is enabled.");
        }
    }

    [ContextMenu("Close Event File")]
    public void CloseEventFile()
    {
        CloseWriter();
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
        P2EventValidationResult validation = contractValidator != null
            ? contractValidator.Validate(simulationEvent)
            : new P2EventValidationResult { isValid = true };

        LogValidation(validation, simulationEvent);

        if (!validation.isValid && !writeWhenValidationFails)
        {
            LogError(
                "Event was not written because contract validation failed. eventType=" +
                (simulationEvent != null ? simulationEvent.eventType : "null")
            );
            return;
        }

        if (!enableFileOutput || fileLoggingDisabledAfterFailure)
        {
            return;
        }

        if (simulationEvent == null)
        {
            return;
        }

        string json;

        try
        {
            // 未使用Payloadを空オブジェクトとして出力しない専用シリアライザーを使用します。
            // JSON Linesでは1イベントを必ず1行にするため、整形出力は使用しません。
            json = P2EventJsonSerializer.ToJson(simulationEvent);
        }
        catch (Exception exception)
        {
            DisableAfterFailure("Event JSON serialization failed.", exception);
            return;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            DisableAfterFailure("Event JSON serialization returned empty text.", null);
            return;
        }

        try
        {
            OpenWriterIfNeeded(simulationEvent);

            if (writer == null)
            {
                return;
            }

            writer.WriteLine(json);

            if (flushAfterEveryEvent)
            {
                writer.Flush();
            }

            writtenEventCount++;

            if (logSuccess)
            {
                Debug.Log(
                    "[P2Event] Written. eventType=" + simulationEvent.eventType +
                    ", sequence=" + simulationEvent.sequenceNumber.ToString(
                        CultureInfo.InvariantCulture
                    ) +
                    ", path=" + currentFilePath
                );
            }
        }
        catch (Exception exception)
        {
            DisableAfterFailure("Event file output failed.", exception);
        }
    }

    private void OpenWriterIfNeeded(P2SimulationEvent simulationEvent)
    {
        string runId = simulationEvent != null
            ? simulationEvent.runId
            : string.Empty;

        if (writer != null &&
            string.Equals(openedRunId, runId, StringComparison.Ordinal))
        {
            return;
        }

        CloseWriter();

        string directory = OutputDirectoryPath;
        Directory.CreateDirectory(directory);

        string scenarioId = simulationEvent != null
            ? simulationEvent.scenarioId
            : "scenario";
        string fileName =
            SanitizePathPart(fileNamePrefix, "events") + "-" +
            SanitizePathPart(scenarioId, "scenario") + "-" +
            SanitizePathPart(runId, "run") + ".jsonl";

        currentFilePath = Path.Combine(directory, fileName);

        FileStream stream = new FileStream(
            currentFilePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read
        );

        // JSON/JSONLは相互運用性を優先し、UTF-8 BOMなしで出力します。
        writer = new StreamWriter(stream, new UTF8Encoding(false));
        openedRunId = runId;
    }

    private void CloseWriter()
    {
        if (writer != null)
        {
            try
            {
                writer.Flush();
                writer.Dispose();
            }
            catch (Exception exception)
            {
                if (logErrors)
                {
                    Debug.LogWarning("[P2Event] Event writer close failed: " + exception.Message);
                }
            }
        }

        writer = null;
        openedRunId = string.Empty;
    }

    private void DisableAfterFailure(string message, Exception exception)
    {
        CloseWriter();
        fileLoggingDisabledAfterFailure = true;

        if (!logErrors)
        {
            return;
        }

        Debug.LogWarning(
            "[P2Event] " + message +
            " File logging is disabled for this run. " +
            "Use 'Retry Event File Logging' after fixing the cause." +
            (exception != null ? " Exception=" + exception.Message : string.Empty)
        );
    }

    private void LogValidation(
        P2EventValidationResult validation,
        P2SimulationEvent simulationEvent)
    {
        if (validation == null)
        {
            return;
        }

        if (validation.errors != null && validation.errors.Count > 0 && logErrors)
        {
            foreach (string error in validation.errors)
            {
                Debug.LogError(
                    "[P2Event] Validation error: " + error +
                    " eventType=" +
                    (simulationEvent != null ? simulationEvent.eventType : "null")
                );
            }
        }

        if (validation.warnings != null &&
            validation.warnings.Count > 0 &&
            logValidationWarnings)
        {
            foreach (string warning in validation.warnings)
            {
                Debug.LogWarning(
                    "[P2Event] Validation warning: " + warning +
                    " eventType=" +
                    (simulationEvent != null ? simulationEvent.eventType : "null")
                );
            }
        }
    }

    private void LogError(string message)
    {
        if (logErrors)
        {
            Debug.LogError("[P2Event] " + message);
        }
    }

    private static string SanitizePathPart(string value, string fallback)
    {
        string result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(invalid, '-');
        }

        result = result.Replace(' ', '-');
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }
}
