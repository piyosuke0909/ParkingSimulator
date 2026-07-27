using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(-925)]
public class ScenarioRunLogger : MonoBehaviour
{
    [Header("Output")]
    public bool logToConsole = true;
    public bool logToFile = true;
    public string fileNamePrefix = "p1-scenario";

    [Header("Runtime (Read Only)")]
    [SerializeField]
    private string currentLogFilePath;

    [SerializeField]
    private bool fileLoggingDisabledAfterFailure;

    private StreamWriter writer;

    public string CurrentLogFilePath => currentLogFilePath;
    public bool FileLoggingDisabledAfterFailure => fileLoggingDisabledAfterFailure;

    private void Awake()
    {
        OpenWriterIfNeeded();
    }

    private void OnDestroy()
    {
        CloseWriter();
    }

    private void OnApplicationQuit()
    {
        CloseWriter();
    }

    public void LogScenarioStarted(LocalScenarioDefinition definition, int seed)
    {
        string scenarioId = definition != null ? definition.scenarioId : "scenario-local";
        Write("scenario.started", $"scenarioId={scenarioId} seed={seed}");
    }

    public void LogFactorActivated(float simulationTime, ScenarioFactorDefinition factor)
    {
        if (factor == null)
        {
            return;
        }

        Write(
            "scenario_factor.activated",
            $"simulationTimeSeconds={simulationTime:F3} scenarioFactorId={factor.scenarioFactorId} " +
            $"factorCode={factor.factorCode} intensity={factor.intensity:F3}"
        );
    }

    public void LogFactorDeactivated(float simulationTime, ScenarioFactorDefinition factor)
    {
        if (factor == null)
        {
            return;
        }

        Write(
            "scenario_factor.deactivated",
            $"simulationTimeSeconds={simulationTime:F3} scenarioFactorId={factor.scenarioFactorId} " +
            $"factorCode={factor.factorCode} reasonCode=end_time"
        );
    }

    public void LogArrivalScheduled(
        float simulationTime,
        string arrivalRateSourceId,
        ArrivalDistribution distribution,
        float baseVehiclesPerMinute,
        float arrivalRateMultiplier,
        float effectiveVehiclesPerMinute,
        float nextDelaySeconds)
    {
        string sourceId = string.IsNullOrEmpty(arrivalRateSourceId)
            ? "default"
            : arrivalRateSourceId;

        Write(
            "arrival.scheduled",
            $"simulationTimeSeconds={simulationTime:F3} arrivalRateSourceId={sourceId} " +
            $"arrivalRateSegmentId={sourceId} " +
            $"distribution={distribution.ToString().ToLowerInvariant()} " +
            $"baseVehiclesPerMinute={baseVehiclesPerMinute:F4} " +
            $"arrivalRateMultiplier={arrivalRateMultiplier:F4} " +
            $"effectiveVehiclesPerMinute={effectiveVehiclesPerMinute:F4} " +
            $"nextDelaySeconds={nextDelaySeconds:F4}"
        );
    }

    public void LogEntranceSelected(
        float simulationTime,
        List<string> availableScenePairNames,
        List<float> weights,
        string selectedScenePairName,
        string selectedCanonicalAccessPointId)
    {
        Write(
            "entrance.selected",
            $"simulationTimeSeconds={simulationTime:F3} " +
            $"availableScenePairs=[{JoinStrings(availableScenePairNames)}] " +
            $"weights=[{JoinWeights(weights)}] " +
            $"selectedScenePairName={selectedScenePairName} " +
            $"selectedAccessPointId={selectedCanonicalAccessPointId}"
        );
    }

    public void LogParkingAreaSelected(
        float simulationTime,
        List<string> availableCanonicalAreaIds,
        List<int> availableSlotCounts,
        List<float> weights,
        string selectedCanonicalAreaId,
        string selectedSceneAreaId)
    {
        Write(
            "parking_area.selected",
            $"simulationTimeSeconds={simulationTime:F3} " +
            $"availableAreaIds=[{JoinStrings(availableCanonicalAreaIds)}] " +
            $"availableSlotCounts=[{JoinIntegers(availableSlotCounts)}] " +
            $"weights=[{JoinWeights(weights)}] " +
            $"selectedAreaId={selectedCanonicalAreaId} " +
            $"selectedSceneAreaId={selectedSceneAreaId}"
        );
    }

    public void LogVehicleSpawned(
        int sequenceNumber,
        float simulationTime,
        int seed,
        string activeFactorIds,
        float arrivalRateMultiplier,
        float speedMultiplier,
        float effectiveVehiclesPerMinute,
        string canonicalAccessPointId,
        string scenePairName,
        string slotId,
        string areaId,
        int activeVehicleCount,
        int maxConcurrentVehicles)
    {
        Write(
            "vehicle.spawned",
            $"sequenceNumber={sequenceNumber} simulationTimeSeconds={simulationTime:F3} seed={seed} " +
            $"activeScenarioFactorIds=[{activeFactorIds}] arrivalRateMultiplier={arrivalRateMultiplier:F4} " +
            $"effectiveVehiclesPerMinute={effectiveVehiclesPerMinute:F4} " +
            $"speedMultiplier={speedMultiplier:F4} accessPointId={canonicalAccessPointId} " +
            $"scenePairName={scenePairName} slotId={slotId} areaId={areaId} " +
            $"activeVehicleCount={activeVehicleCount} maxConcurrentVehicles={maxConcurrentVehicles}"
        );
    }

    public void LogVehicleRemoved(
        float simulationTime,
        string vehicleName,
        int activeVehicleCount,
        int maxConcurrentVehicles)
    {
        Write(
            "vehicle.removed",
            $"simulationTimeSeconds={simulationTime:F3} vehicleName={vehicleName} " +
            $"activeVehicleCount={activeVehicleCount} maxConcurrentVehicles={maxConcurrentVehicles}"
        );
    }

    public void LogScenarioCompleted(float simulationTime, string reason)
    {
        Write(
            "scenario.completed",
            $"simulationTimeSeconds={simulationTime:F3} reason={reason}"
        );
    }

    public void LogWarning(string message)
    {
        Write("warning", message);
    }

    private static string JoinStrings(List<string> values)
    {
        return values != null ? string.Join(",", values) : string.Empty;
    }

    private static string JoinWeights(List<float> weights)
    {
        List<string> formatted = new List<string>();

        if (weights != null)
        {
            foreach (float weight in weights)
            {
                formatted.Add(weight.ToString("F4"));
            }
        }

        return string.Join(",", formatted);
    }

    private static string JoinIntegers(List<int> values)
    {
        if (values == null)
        {
            return string.Empty;
        }

        List<string> formatted = new List<string>(values.Count);

        foreach (int value in values)
        {
            formatted.Add(value.ToString());
        }

        return string.Join(",", formatted);
    }

    private void Write(string eventType, string body)
    {
        string line = $"[P1Scenario] eventType={eventType} {body}";

        if (logToConsole)
        {
            Debug.Log(line);
        }

        if (!logToFile)
        {
            return;
        }

        OpenWriterIfNeeded();

        if (writer == null)
        {
            return;
        }

        writer.WriteLine(line);
        writer.Flush();
    }

    private void OpenWriterIfNeeded()
    {
        if (!logToFile || writer != null || fileLoggingDisabledAfterFailure)
        {
            return;
        }

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            return;
        }

        try
        {
            string directory = Path.Combine(Application.persistentDataPath, "P1ScenarioLogs");
            Directory.CreateDirectory(directory);

            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            currentLogFilePath = Path.Combine(directory, $"{fileNamePrefix}-{timestamp}.log");
            writer = new StreamWriter(currentLogFilePath, false, new UTF8Encoding(false));
            fileLoggingDisabledAfterFailure = false;
        }
        catch (Exception exception)
        {
            fileLoggingDisabledAfterFailure = true;
            writer = null;
            Debug.LogWarning(
                $"{name}: P1ログファイルを開けなかったため、この実行中のファイル出力を停止します。" +
                $"再試行するには Retry File Logging を実行してください。{exception.Message}"
            );
        }
    }

    [ContextMenu("Retry File Logging")]
    public void RetryFileLogging()
    {
        CloseWriter();
        fileLoggingDisabledAfterFailure = false;
        currentLogFilePath = string.Empty;
        OpenWriterIfNeeded();
    }

    private void CloseWriter()
    {
        if (writer == null)
        {
            return;
        }

        writer.Flush();
        writer.Dispose();
        writer = null;
    }
}
