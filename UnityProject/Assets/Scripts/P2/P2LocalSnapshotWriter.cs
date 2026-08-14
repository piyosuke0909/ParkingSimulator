using System;
using System.IO;
using System.Globalization;
using System.Text;
using UnityEngine;

public enum P2SnapshotIntervalMode
{
    SimulationSeconds,
    RealSeconds
}

public class P2LocalSnapshotWriter : MonoBehaviour
{
    public event Action<P2SimulationSnapshot, string> SnapshotReady;

    [Header("Components")]
    public P2SimulationSnapshotBuilder snapshotBuilder;
    public P2SnapshotContractValidator contractValidator;
    public SimulationClock simulationClock;

    [Header("Automatic Output")]
    public bool writeOnStart = true;
    public bool writeRepeatedly = true;
    public P2SnapshotIntervalMode intervalMode = P2SnapshotIntervalMode.SimulationSeconds;

    [Min(0.1f)]
    public float writeIntervalSeconds = 60f;

    public bool stopAfterScenarioCompleted = true;

    [Header("File Output")]
    public string outputDirectoryName = "P2Snapshots";
    public string fileNamePrefix = "snapshot";
    public bool prettyPrint = true;
    public bool keepSequenceFiles = true;
    public bool writeLatestCopy = true;
    public bool writeWhenValidationFails = false;

    [Header("Logging")]
    public bool logSuccess = true;
    public bool logValidationWarnings = true;
    public bool logErrors = true;

    [Header("Runtime (Read Only)")]
    [SerializeField]
    private long sequenceNumber;

    [SerializeField]
    private string lastWrittenPath;

    [SerializeField]
    private string currentRunId;

    private double nextSimulationWriteAt;
    private double previousTotalAdvancedSimulationSeconds;
    private float nextRealWriteAt;
    private bool automaticOutputStopped;
    private bool completionSnapshotWritten;

    public long SequenceNumber => sequenceNumber;
    public string LastWrittenPath => lastWrittenPath;
    public string OutputDirectoryPath => Path.Combine(
        Application.persistentDataPath,
        SanitizeDirectoryName(outputDirectoryName)
    );

    private void Awake()
    {
        ResolveReferences();
        ResetAutomaticSchedule();
    }

    private void Start()
    {
        if (writeOnStart)
        {
            WriteSnapshotNow();
        }
    }

    private void Update()
    {
        if (!writeRepeatedly || automaticOutputStopped)
        {
            return;
        }

        ResolveReferences();

        if (stopAfterScenarioCompleted &&
            snapshotBuilder != null &&
            snapshotBuilder.scenarioRuntime != null &&
            snapshotBuilder.scenarioRuntime.IsScenarioCompleted)
        {
            if (!completionSnapshotWritten)
            {
                WriteSnapshotNow();
            }

            automaticOutputStopped = true;
            return;
        }

        if (intervalMode == P2SnapshotIntervalMode.SimulationSeconds && simulationClock != null)
        {
            UpdateSimulationInterval();
        }
        else
        {
            UpdateRealInterval();
        }
    }

    [ContextMenu("Write Snapshot Now")]
    public void WriteSnapshotNowFromContextMenu()
    {
        WriteSnapshotNow();
    }

    public bool WriteSnapshotNow()
    {
        ResolveReferences();

        if (snapshotBuilder == null)
        {
            LogError("P2SimulationSnapshotBuilder is not configured.");
            return false;
        }

        P2SimulationRunIdentity runIdentity = P2SimulationRunContext.EnsureCurrentRun();
        bool runChanged = !string.Equals(
            currentRunId,
            runIdentity.runId,
            StringComparison.Ordinal
        );
        long candidateSequence = runChanged ? 1L : sequenceNumber + 1L;
        P2SimulationSnapshot snapshot;

        try
        {
            snapshot = snapshotBuilder.BuildSnapshot(candidateSequence);
        }
        catch (Exception exception)
        {
            LogError("Snapshot build failed: " + exception);
            return false;
        }

        P2SnapshotValidationResult validation = contractValidator != null
            ? contractValidator.Validate(snapshot)
            : new P2SnapshotValidationResult { isValid = true };

        LogValidation(validation);

        if (!validation.isValid && !writeWhenValidationFails)
        {
            LogError("Snapshot was not written because contract validation failed.");
            return false;
        }

        string json;

        try
        {
            json = JsonUtility.ToJson(snapshot, prettyPrint);
        }
        catch (Exception exception)
        {
            LogError("Snapshot JSON serialization failed: " + exception);
            return false;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            LogError("Snapshot JSON serialization returned an empty string.");
            return false;
        }

        NotifySnapshotReady(snapshot, json);

        string directory = OutputDirectoryPath;
        string scenarioId = snapshot.scenario != null
            ? snapshot.scenario.scenarioId
            : "scenario";
        string timeText = snapshot.scenario != null
            ? Math.Max(0d, snapshot.scenario.simulationTimeSeconds).ToString(
                "0000000000.000",
                CultureInfo.InvariantCulture
            )
            : "0000000000.000";

        string validitySuffix = validation.isValid ? string.Empty : "-invalid";
        string fileName =
            SanitizeFileName(fileNamePrefix) + "-" +
            SanitizeFileName(scenarioId) + "-" +
            SanitizeFileName(snapshot.runId) + "-t" +
            timeText.Replace('.', '_') + "-seq" +
            candidateSequence.ToString("D8") + validitySuffix + ".json";

        try
        {
            Directory.CreateDirectory(directory);

            if (keepSequenceFiles)
            {
                string sequencePath = Path.Combine(directory, fileName);
                WriteTextAtomically(sequencePath, json);
                lastWrittenPath = sequencePath;
            }

            if (writeLatestCopy)
            {
                string latestPath = Path.Combine(
                    directory,
                    SanitizeFileName(fileNamePrefix) + "-latest.json"
                );
                WriteTextAtomically(latestPath, json);

                if (!keepSequenceFiles)
                {
                    lastWrittenPath = latestPath;
                }
            }

            if (!keepSequenceFiles && !writeLatestCopy)
            {
                LogError("Both keepSequenceFiles and writeLatestCopy are disabled.");
                return false;
            }
        }
        catch (Exception exception)
        {
            LogError("Snapshot file output failed: " + exception);
            return false;
        }

        currentRunId = snapshot.runId;
        sequenceNumber = candidateSequence;
        completionSnapshotWritten = snapshot.scenario != null && snapshot.scenario.isCompleted;

        if (logSuccess)
        {
            Debug.Log(
                "[P2Snapshot] Written. " +
                "runId=" + currentRunId +
                ", sequence=" + sequenceNumber +
                ", simulationTimeSeconds=" +
                (snapshot.scenario != null
                    ? snapshot.scenario.simulationTimeSeconds.ToString(
                        "F3",
                        CultureInfo.InvariantCulture
                    )
                    : "0.000") +
                ", vehicles=" +
                (snapshot.vehicles != null ? snapshot.vehicles.Count : 0) +
                ", slots=" +
                (snapshot.parkingSlots != null ? snapshot.parkingSlots.Count : 0) +
                ", path=" + lastWrittenPath
            );
        }

        return true;
    }

    [ContextMenu("Open Snapshot Output Folder")]
    public void OpenSnapshotOutputFolder()
    {
        string path = OutputDirectoryPath;
        Directory.CreateDirectory(path);
        Application.OpenURL("file:///" + path.Replace('\\', '/'));
    }

    [ContextMenu("Reset Snapshot Sequence")]
    public void ResetSnapshotSequence()
    {
        if (Application.isPlaying && !string.IsNullOrWhiteSpace(currentRunId))
        {
            Debug.LogWarning(
                "[P2Snapshot] The sequence is scoped to the current run and was not reset. " +
                "Start a new simulation run to begin again from sequence 1 without overwriting files."
            );
            return;
        }

        sequenceNumber = 0L;
        currentRunId = string.Empty;
        lastWrittenPath = string.Empty;
        automaticOutputStopped = false;
        completionSnapshotWritten = false;
        ResetAutomaticSchedule();
    }

    [ContextMenu("Resolve Snapshot References")]
    public void ResolveReferences()
    {
        if (snapshotBuilder == null)
        {
            snapshotBuilder = GetComponent<P2SimulationSnapshotBuilder>();
        }

        if (contractValidator == null)
        {
            contractValidator = GetComponent<P2SnapshotContractValidator>();
        }

        if (snapshotBuilder == null)
        {
#if UNITY_2023_1_OR_NEWER
            snapshotBuilder = FindFirstObjectByType<P2SimulationSnapshotBuilder>();
#else
            snapshotBuilder = FindObjectOfType<P2SimulationSnapshotBuilder>();
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

        if (simulationClock == null && snapshotBuilder != null)
        {
            snapshotBuilder.ResolveReferences();
            simulationClock = snapshotBuilder.simulationClock;
        }

        if (simulationClock == null)
        {
#if UNITY_2023_1_OR_NEWER
            simulationClock = FindFirstObjectByType<SimulationClock>();
#else
            simulationClock = FindObjectOfType<SimulationClock>();
#endif
        }
    }

    private void UpdateSimulationInterval()
    {
        double totalAdvanced = simulationClock.TotalAdvancedSimulationSeconds;
        double interval = Math.Max(0.1d, writeIntervalSeconds);

        if (totalAdvanced < previousTotalAdvancedSimulationSeconds)
        {
            nextSimulationWriteAt = totalAdvanced + interval;
        }

        previousTotalAdvancedSimulationSeconds = totalAdvanced;

        if (totalAdvanced < nextSimulationWriteAt)
        {
            return;
        }

        WriteSnapshotNow();

        do
        {
            nextSimulationWriteAt += interval;
        }
        while (nextSimulationWriteAt <= totalAdvanced);
    }

    private void UpdateRealInterval()
    {
        if (Time.unscaledTime < nextRealWriteAt)
        {
            return;
        }

        WriteSnapshotNow();
        nextRealWriteAt = Time.unscaledTime + Mathf.Max(0.1f, writeIntervalSeconds);
    }

    private void ResetAutomaticSchedule()
    {
        double interval = Math.Max(0.1d, writeIntervalSeconds);
        double currentAdvanced = simulationClock != null
            ? simulationClock.TotalAdvancedSimulationSeconds
            : 0d;

        previousTotalAdvancedSimulationSeconds = currentAdvanced;
        nextSimulationWriteAt = currentAdvanced + interval;
        nextRealWriteAt = Time.unscaledTime + Mathf.Max(0.1f, writeIntervalSeconds);
    }

    private void LogValidation(P2SnapshotValidationResult validation)
    {
        if (validation == null)
        {
            return;
        }

        if (validation.errors != null)
        {
            foreach (string error in validation.errors)
            {
                LogError("Contract error: " + error);
            }
        }

        if (logValidationWarnings && validation.warnings != null)
        {
            foreach (string warning in validation.warnings)
            {
                Debug.LogWarning("[P2Snapshot] Contract warning: " + warning);
            }
        }
    }

    private void LogError(string message)
    {
        if (logErrors)
        {
            Debug.LogError("[P2Snapshot] " + message);
        }
    }

    private void NotifySnapshotReady(P2SimulationSnapshot snapshot, string json)
    {
        Action<P2SimulationSnapshot, string> handlers = SnapshotReady;
        if (handlers == null)
        {
            return;
        }

        foreach (Delegate subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((Action<P2SimulationSnapshot, string>)subscriber)(snapshot, json);
            }
            catch (Exception exception)
            {
                LogError(
                    "A SnapshotReady subscriber failed. Local Snapshot output continues. " +
                    exception
                );
            }
        }
    }

    private static void WriteTextAtomically(string path, string text)
    {
        string temporaryPath = path + ".tmp";
        // JSONはRFC 8259に合わせ、UTF-8（BOMなし）で保存します。
        File.WriteAllText(temporaryPath, text, new UTF8Encoding(false));

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(temporaryPath, path);
    }

    private static string SanitizeDirectoryName(string value)
    {
        string sanitized = SanitizeFileName(value);
        return string.IsNullOrWhiteSpace(sanitized) ? "P2Snapshots" : sanitized;
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "snapshot";
        }

        string result = value.Trim();

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(invalid, '-');
        }

        result = result.Replace(' ', '-');
        return string.IsNullOrWhiteSpace(result) ? "snapshot" : result;
    }
}
