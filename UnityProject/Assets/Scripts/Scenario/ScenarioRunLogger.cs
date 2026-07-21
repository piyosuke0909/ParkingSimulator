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

    private StreamWriter writer;

    public string CurrentLogFilePath => currentLogFilePath;

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

    public void LogEntranceSelected(
        float simulationTime,
        List<string> availableScenePairNames,
        List<float> weights,
        string selectedScenePairName,
        string selectedCanonicalAccessPointId)
    {
        string availablePairsText = availableScenePairNames != null
            ? string.Join(",", availableScenePairNames)
            : string.Empty;

        List<string> formattedWeights = new List<string>();

        if (weights != null)
        {
            foreach (float weight in weights)
            {
                formattedWeights.Add(weight.ToString("F4"));
            }
        }

        Write(
            "entrance.selected",
            $"simulationTimeSeconds={simulationTime:F3} " +
            $"availableScenePairs=[{availablePairsText}] weights=[{string.Join(",", formattedWeights)}] " +
            $"selectedScenePairName={selectedScenePairName} " +
            $"selectedAccessPointId={selectedCanonicalAccessPointId}"
        );
    }

    public void LogVehicleSpawned(
        int sequenceNumber,
        float simulationTime,
        int seed,
        string activeFactorIds,
        float arrivalRateMultiplier,
        float speedMultiplier,
        string canonicalAccessPointId,
        string scenePairName,
        string slotId,
        string areaId)
    {
        Write(
            "vehicle.spawned",
            $"sequenceNumber={sequenceNumber} simulationTimeSeconds={simulationTime:F3} seed={seed} " +
            $"activeScenarioFactorIds=[{activeFactorIds}] arrivalRateMultiplier={arrivalRateMultiplier:F4} " +
            $"speedMultiplier={speedMultiplier:F4} accessPointId={canonicalAccessPointId} " +
            $"scenePairName={scenePairName} slotId={slotId} areaId={areaId}"
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
        if (!logToFile || writer != null)
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
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"{name}: P1ログファイルを開けませんでした。{exception.Message}");
            writer = null;
        }
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
