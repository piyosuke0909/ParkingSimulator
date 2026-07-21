using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class SimulationClock : MonoBehaviour
{
    [Header("Scenario")]
    public LocalScenarioDefinition scenarioDefinition;

    [Header("Clock")]
    public bool autoStart = true;
    public bool useUnscaledDeltaTime = false;

    [Min(0f)]
    public float simulationTimeScale = 1f;

    [Min(0f)]
    public float startSimulationTimeSeconds;

    public bool loopAtScenarioEnd = false;

    [Header("Debug")]
    [Min(0f)]
    public float debugSetTimeSeconds = 1795f;

    [Header("Runtime (Read Only)")]
    [SerializeField]
    private float simulationTimeSeconds;

    [SerializeField]
    private bool isRunning;

    public float SimulationTimeSeconds => simulationTimeSeconds;
    public float SimulationTimeScale => simulationTimeScale;
    public bool IsRunning => isRunning;

    private void Awake()
    {
        ResetClock();

        if (autoStart)
        {
            StartClock();
        }
    }

    private void Update()
    {
        if (!isRunning || simulationTimeScale <= 0f)
        {
            return;
        }

        float delta = useUnscaledDeltaTime
            ? Time.unscaledDeltaTime
            : Time.deltaTime;

        simulationTimeSeconds += delta * simulationTimeScale;

        float duration = scenarioDefinition != null
            ? scenarioDefinition.durationSeconds
            : 0f;

        if (duration <= 0f || simulationTimeSeconds < duration)
        {
            return;
        }

        if (loopAtScenarioEnd)
        {
            simulationTimeSeconds %= duration;
        }
        else
        {
            simulationTimeSeconds = duration;
            isRunning = false;
        }
    }

    [ContextMenu("Start Clock")]
    public void StartClock()
    {
        isRunning = true;
    }

    [ContextMenu("Pause Clock")]
    public void PauseClock()
    {
        isRunning = false;
    }

    [ContextMenu("Reset Clock")]
    public void ResetClock()
    {
        simulationTimeSeconds = Mathf.Max(0f, startSimulationTimeSeconds);
        isRunning = false;
    }

    [ContextMenu("Apply Debug Time")]
    public void ApplyDebugTime()
    {
        SetSimulationTime(debugSetTimeSeconds);
    }

    public void SetSimulationTime(float seconds)
    {
        simulationTimeSeconds = Mathf.Max(0f, seconds);
    }
}
