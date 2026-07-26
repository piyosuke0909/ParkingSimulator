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

    // 表示時刻とは別に、Clockが実際に前進させたシミュレーション秒数を保持します。
    // SetSimulationTimeによる手動ジャンプや時刻の巻き戻しは加算しません。
    private double totalAdvancedSimulationSeconds;

    public float SimulationTimeSeconds => simulationTimeSeconds;
    public float SimulationTimeScale => simulationTimeScale;
    public bool IsRunning => isRunning;
    public double TotalAdvancedSimulationSeconds => totalAdvancedSimulationSeconds;

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

        float requestedAdvance = delta * simulationTimeScale;

        if (requestedAdvance <= 0f ||
            float.IsNaN(requestedAdvance) ||
            float.IsInfinity(requestedAdvance))
        {
            return;
        }

        float duration = scenarioDefinition != null
            ? scenarioDefinition.durationSeconds
            : 0f;

        if (duration <= 0f)
        {
            simulationTimeSeconds += requestedAdvance;
            totalAdvancedSimulationSeconds += requestedAdvance;
            return;
        }

        if (loopAtScenarioEnd)
        {
            simulationTimeSeconds = Mathf.Repeat(
                simulationTimeSeconds + requestedAdvance,
                duration
            );

            // 表示時刻が0秒へ戻っても、実際に進んだ秒数は失わないようにします。
            totalAdvancedSimulationSeconds += requestedAdvance;
            return;
        }

        float remaining = Mathf.Max(0f, duration - simulationTimeSeconds);
        float appliedAdvance = Mathf.Min(requestedAdvance, remaining);

        simulationTimeSeconds += appliedAdvance;
        totalAdvancedSimulationSeconds += appliedAdvance;

        if (simulationTimeSeconds >= duration)
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
        totalAdvancedSimulationSeconds = 0d;
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
