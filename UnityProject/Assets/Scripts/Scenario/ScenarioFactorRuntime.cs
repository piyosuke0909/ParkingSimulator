using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(-900)]
public class ScenarioFactorRuntime : MonoBehaviour
{
    public static ScenarioFactorRuntime Instance { get; private set; }

    [Header("References")]
    public LocalScenarioDefinition scenarioDefinition;
    public SimulationClock simulationClock;
    public ScenarioRandomService randomService;
    public ScenarioTargetBinding targetBinding;
    public ScenarioRunLogger runLogger;

    [Header("Behavior")]
    public bool autoFindReferences = true;
    public bool logUnresolvedAccessPointEffects = true;

    private readonly HashSet<string> activeFactorIds = new HashSet<string>();
    private readonly HashSet<string> unresolvedEffectWarnings = new HashSet<string>();
    private bool scenarioStartedLogged;
    private bool scenarioCompletedLogged;

    public LocalScenarioDefinition Definition => scenarioDefinition;
    public SimulationClock Clock => simulationClock;
    public ScenarioRandomService RandomService => randomService;
    public ScenarioRunLogger RunLogger => runLogger;

    public float SimulationTimeSeconds => simulationClock != null
        ? simulationClock.SimulationTimeSeconds
        : Time.time;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"{name}: ScenarioFactorRuntime が複数存在します。先に初期化されたものを使用します。");
        }
        else
        {
            Instance = this;
        }

        ResolveReferences();
        SynchronizeDefinitions();
    }

    private void Start()
    {
        if (randomService != null)
        {
            randomService.InitializeFromDefinition();
        }

        if (runLogger != null && !scenarioStartedLogged)
        {
            runLogger.LogScenarioStarted(
                scenarioDefinition,
                randomService != null ? randomService.CurrentSeed : 0
            );
            scenarioStartedLogged = true;
        }

        EvaluateFactorTransitions(true);
    }

    private void Update()
    {
        EvaluateFactorTransitions(false);

        if (!scenarioCompletedLogged &&
            scenarioDefinition != null &&
            SimulationTimeSeconds >= scenarioDefinition.durationSeconds)
        {
            runLogger?.LogScenarioCompleted(SimulationTimeSeconds, "duration_reached");
            scenarioCompletedLogged = true;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    [ContextMenu("Reset Scenario Runtime")]
    public void ResetRuntime()
    {
        activeFactorIds.Clear();
        unresolvedEffectWarnings.Clear();
        scenarioCompletedLogged = false;

        if (simulationClock != null)
        {
            simulationClock.ResetClock();

            if (simulationClock.autoStart)
            {
                simulationClock.StartClock();
            }
        }

        randomService?.InitializeFromDefinition();
        EvaluateFactorTransitions(true);
    }

    public float GetVehicleSpeedMultiplier()
    {
        float value = 1f;

        foreach (ScenarioFactorEffect effect in EnumerateActiveEffects(ScenarioMetrics.Speed))
        {
            if (effect.targetType != FactorEffectTargetType.VehiclePopulation &&
                effect.targetType != FactorEffectTargetType.Facility)
            {
                continue;
            }

            value = Apply(value, effect);
        }

        return Mathf.Max(0f, value);
    }

    public float GetEffectiveVehicleSpeed(float baseSpeed)
    {
        return Mathf.Max(0f, baseSpeed * GetVehicleSpeedMultiplier());
    }

    public float GetArrivalRateMultiplier(string scenePairName)
    {
        float value = 1f;

        foreach (ScenarioFactorEffect effect in EnumerateActiveEffects(ScenarioMetrics.ArrivalRate))
        {
            if (effect.targetType == FactorEffectTargetType.Facility ||
                effect.targetType == FactorEffectTargetType.VehiclePopulation)
            {
                value = Apply(value, effect);
                continue;
            }

            if (effect.targetType == FactorEffectTargetType.AccessPoint &&
                MatchesAccessPoint(effect.targetId, scenePairName))
            {
                value = Apply(value, effect);
            }
        }

        return Mathf.Max(0f, value);
    }

    public float GetAccessPointPreferenceWeight(string scenePairName)
    {
        float value = 1f;

        foreach (ScenarioFactorEffect effect in EnumerateActiveEffects(ScenarioMetrics.ParkingPreferenceWeight))
        {
            if (effect.targetType != FactorEffectTargetType.AccessPoint)
            {
                continue;
            }

            if (MatchesAccessPoint(effect.targetId, scenePairName))
            {
                value = Apply(value, effect);
            }
        }

        return Mathf.Max(0f, value);
    }

    public float GetNextSpawnDelay(float fallbackSpawnInterval, string scenePairName)
    {
        float time = SimulationTimeSeconds;
        ArrivalRateSegment segment = FindArrivalRateSegment(time, scenePairName);
        float baseInterval = Mathf.Max(0.01f, fallbackSpawnInterval);
        ArrivalDistribution distribution = ArrivalDistribution.Fixed;

        if (segment != null)
        {
            if (segment.vehiclesPerMinute <= 0f)
            {
                return float.PositiveInfinity;
            }

            baseInterval = 60f / segment.vehiclesPerMinute;
            distribution = segment.distribution;
        }

        float multiplier = GetArrivalRateMultiplier(scenePairName);

        if (multiplier <= 0f)
        {
            return float.PositiveInfinity;
        }

        float meanInterval = baseInterval / multiplier;

        if (distribution == ArrivalDistribution.Poisson && randomService != null)
        {
            float unit = Mathf.Clamp(randomService.NextUnitFloat(), 0.000001f, 0.999999f);
            return Mathf.Max(0.01f, -Mathf.Log(1f - unit) * meanInterval);
        }

        return Mathf.Max(0.01f, meanInterval);
    }

    public string GetCanonicalAccessPointId(string scenePairName)
    {
        if (targetBinding != null)
        {
            return targetBinding.GetPrimaryCanonicalAccessPointId(scenePairName);
        }

        return string.IsNullOrEmpty(scenePairName)
            ? "entrance-unknown"
            : scenePairName;
    }

    public string GetCanonicalAreaId(string sceneAreaId)
    {
        if (targetBinding != null)
        {
            return targetBinding.GetCanonicalAreaId(sceneAreaId);
        }

        return sceneAreaId;
    }

    public string GetActiveFactorIdsCsv()
    {
        List<string> ids = new List<string>(activeFactorIds);
        ids.Sort(StringComparer.Ordinal);
        return string.Join(",", ids);
    }

    public string GetActiveFactorCodesCsv()
    {
        if (scenarioDefinition == null || scenarioDefinition.scenarioFactors == null)
        {
            return string.Empty;
        }

        List<string> codes = new List<string>();

        foreach (ScenarioFactorDefinition factor in scenarioDefinition.scenarioFactors)
        {
            if (factor != null && activeFactorIds.Contains(factor.scenarioFactorId))
            {
                codes.Add(factor.factorCode);
            }
        }

        codes.Sort(StringComparer.Ordinal);
        return string.Join(",", codes);
    }

    private void ResolveReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (simulationClock == null)
        {
            simulationClock = GetComponent<SimulationClock>();
        }

        if (randomService == null)
        {
            randomService = GetComponent<ScenarioRandomService>();
        }

        if (targetBinding == null)
        {
            targetBinding = GetComponent<ScenarioTargetBinding>();
        }

        if (runLogger == null)
        {
            runLogger = GetComponent<ScenarioRunLogger>();
        }
    }

    private void SynchronizeDefinitions()
    {
        if (simulationClock != null && simulationClock.scenarioDefinition == null)
        {
            simulationClock.scenarioDefinition = scenarioDefinition;
        }

        if (randomService != null && randomService.scenarioDefinition == null)
        {
            randomService.scenarioDefinition = scenarioDefinition;
        }
    }

    private void EvaluateFactorTransitions(bool force)
    {
        if (scenarioDefinition == null || scenarioDefinition.scenarioFactors == null)
        {
            return;
        }

        float currentTime = SimulationTimeSeconds;

        foreach (ScenarioFactorDefinition factor in scenarioDefinition.scenarioFactors)
        {
            if (factor == null || string.IsNullOrEmpty(factor.scenarioFactorId))
            {
                continue;
            }

            bool activeNow = factor.IsActiveAt(currentTime);
            bool wasActive = activeFactorIds.Contains(factor.scenarioFactorId);

            if (activeNow && !wasActive)
            {
                activeFactorIds.Add(factor.scenarioFactorId);
                runLogger?.LogFactorActivated(currentTime, factor);
            }
            else if (!activeNow && wasActive)
            {
                activeFactorIds.Remove(factor.scenarioFactorId);
                runLogger?.LogFactorDeactivated(currentTime, factor);
            }
            else if (force && activeNow)
            {
                activeFactorIds.Add(factor.scenarioFactorId);
            }
        }
    }

    private IEnumerable<ScenarioFactorEffect> EnumerateActiveEffects(string metric)
    {
        if (scenarioDefinition == null || scenarioDefinition.scenarioFactors == null)
        {
            yield break;
        }

        float currentTime = SimulationTimeSeconds;

        foreach (ScenarioFactorDefinition factor in scenarioDefinition.scenarioFactors)
        {
            if (factor == null || !factor.IsActiveAt(currentTime) || factor.effects == null)
            {
                continue;
            }

            foreach (ScenarioFactorEffect effect in factor.effects)
            {
                if (effect == null || !effect.enabled)
                {
                    continue;
                }

                if (string.Equals(effect.metric, metric, StringComparison.Ordinal))
                {
                    yield return effect;
                }
            }
        }
    }

    private ArrivalRateSegment FindArrivalRateSegment(float simulationTime, string scenePairName)
    {
        if (scenarioDefinition == null || scenarioDefinition.arrivalRateSegments == null)
        {
            return null;
        }

        ArrivalRateSegment globalMatch = null;
        ArrivalRateSegment specificMatch = null;

        foreach (ArrivalRateSegment segment in scenarioDefinition.arrivalRateSegments)
        {
            if (segment == null || !segment.IsActiveAt(simulationTime))
            {
                continue;
            }

            if (string.IsNullOrEmpty(segment.accessPointId))
            {
                globalMatch = segment;
            }
            else if (MatchesAccessPoint(segment.accessPointId, scenePairName))
            {
                specificMatch = segment;
            }
        }

        return specificMatch ?? globalMatch;
    }

    private bool MatchesAccessPoint(string canonicalTargetId, string scenePairName)
    {
        if (targetBinding != null)
        {
            bool matches = targetBinding.MatchesAccessPointTarget(canonicalTargetId, scenePairName);

            if (!matches &&
                logUnresolvedAccessPointEffects &&
                !string.IsNullOrEmpty(canonicalTargetId) &&
                !targetBinding.HasResolvedAccessPoint(canonicalTargetId) &&
                unresolvedEffectWarnings.Add(canonicalTargetId))
            {
                runLogger?.LogWarning(
                    $"unresolvedAccessPointId={canonicalTargetId} " +
                    "ScenarioTargetBindingでscenePairNameを設定してください。"
                );
            }

            return matches;
        }

        return string.Equals(canonicalTargetId, scenePairName, StringComparison.Ordinal);
    }

    private static float Apply(float currentValue, ScenarioFactorEffect effect)
    {
        switch (effect.operation)
        {
            case FactorEffectOperation.Add:
                return currentValue + effect.value;

            case FactorEffectOperation.Set:
                return effect.value;

            case FactorEffectOperation.Multiply:
            default:
                return currentValue * effect.value;
        }
    }
}
