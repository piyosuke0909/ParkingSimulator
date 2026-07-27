using System;
using System.Collections.Generic;
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

    [Tooltip("ONの場合はArrivalRateSegment区間外で停止します。OFFの場合はVehicleSpawnManager.spawnIntervalを既定流入として使用します。")]
    public bool requireActiveArrivalRateSegment = false;

    public bool logUnresolvedAccessPointEffects = true;
    public bool logUnresolvedAreaEffects = true;

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

    public bool IsScenarioCompleted =>
        scenarioDefinition != null &&
        SimulationTimeSeconds >= scenarioDefinition.durationSeconds;

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

        if (!scenarioCompletedLogged && IsScenarioCompleted)
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

    public float GetArrivalRateMultiplier(string scenePairName = null)
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

    public float GetAreaPreferenceWeight(string sceneAreaId)
    {
        float value = 1f;

        foreach (ScenarioFactorEffect effect in EnumerateActiveEffects(ScenarioMetrics.ParkingPreferenceWeight))
        {
            if (effect.targetType != FactorEffectTargetType.Area)
            {
                continue;
            }

            if (MatchesArea(effect.targetId, sceneAreaId))
            {
                value = Apply(value, effect);
            }
        }

        return Mathf.Max(0f, value);
    }

    public ArrivalRateSegment GetActiveArrivalRateSegment(string scenePairName = null)
    {
        return FindArrivalRateSegment(SimulationTimeSeconds, scenePairName);
    }

    public bool HasActiveArrivalRateSegment(string scenePairName = null)
    {
        if (scenarioDefinition == null ||
            scenarioDefinition.arrivalRateSegments == null ||
            scenarioDefinition.arrivalRateSegments.Count == 0)
        {
            return !requireActiveArrivalRateSegment;
        }

        return GetActiveArrivalRateSegment(scenePairName) != null;
    }

    public float GetBaseVehiclesPerMinute(float fallbackSpawnInterval, string scenePairName = null)
    {
        ArrivalRateSegment segment = GetActiveArrivalRateSegment(scenePairName);

        if (segment != null)
        {
            return Mathf.Max(0f, segment.vehiclesPerMinute);
        }

        bool hasConfiguredSegments = scenarioDefinition != null &&
                                     scenarioDefinition.arrivalRateSegments != null &&
                                     scenarioDefinition.arrivalRateSegments.Count > 0;

        if (requireActiveArrivalRateSegment && hasConfiguredSegments)
        {
            return 0f;
        }

        float safeFallbackInterval = Mathf.Max(0.01f, fallbackSpawnInterval);
        return 60f / safeFallbackInterval;
    }

    public ArrivalDistribution GetArrivalDistribution(string scenePairName = null)
    {
        ArrivalRateSegment segment = GetActiveArrivalRateSegment(scenePairName);
        return segment != null ? segment.distribution : ArrivalDistribution.Fixed;
    }

    public string GetArrivalRateSourceId(string scenePairName = null)
    {
        ArrivalRateSegment segment = GetActiveArrivalRateSegment(scenePairName);
        return segment != null && !string.IsNullOrEmpty(segment.arrivalRateSegmentId)
            ? segment.arrivalRateSegmentId
            : "default";
    }

    public float GetEffectiveVehiclesPerMinute(float fallbackSpawnInterval, string scenePairName = null)
    {
        float baseVehiclesPerMinute = GetBaseVehiclesPerMinute(fallbackSpawnInterval, scenePairName);
        return Mathf.Max(0f, baseVehiclesPerMinute * GetArrivalRateMultiplier(scenePairName));
    }

    // 既存コード互換用。Segment不在時の既定値を取得する場合は、
    // fallbackSpawnIntervalを受け取るオーバーロードを使用してください。
    public float GetEffectiveVehiclesPerMinute(string scenePairName = null)
    {
        ArrivalRateSegment segment = GetActiveArrivalRateSegment(scenePairName);

        if (segment == null || segment.vehiclesPerMinute <= 0f)
        {
            return 0f;
        }

        return Mathf.Max(0f, segment.vehiclesPerMinute * GetArrivalRateMultiplier(scenePairName));
    }

    public float GetNextSpawnDelay(float fallbackSpawnInterval, string scenePairName = null)
    {
        float effectiveVehiclesPerMinute = GetEffectiveVehiclesPerMinute(
            fallbackSpawnInterval,
            scenePairName
        );

        if (effectiveVehiclesPerMinute <= 0f)
        {
            return float.PositiveInfinity;
        }

        float meanInterval = 60f / effectiveVehiclesPerMinute;
        ArrivalDistribution distribution = GetArrivalDistribution(scenePairName);

        if (distribution == ArrivalDistribution.Poisson && randomService != null)
        {
            return Mathf.Max(0.01f, randomService.SampleExponential(meanInterval));
        }

        return Mathf.Max(0.01f, meanInterval);
    }

    public float GetSecondsUntilNextArrivalSegmentOrScenarioEnd(string scenePairName = null)
    {
        if (IsScenarioCompleted)
        {
            return 0f;
        }

        float currentTime = SimulationTimeSeconds;
        float nextStart = float.PositiveInfinity;

        if (scenarioDefinition != null && scenarioDefinition.arrivalRateSegments != null)
        {
            foreach (ArrivalRateSegment segment in scenarioDefinition.arrivalRateSegments)
            {
                if (segment == null || !segment.enabled || segment.endSimulationTimeSeconds <= currentTime)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(segment.accessPointId) &&
                    !MatchesAccessPoint(segment.accessPointId, scenePairName))
                {
                    continue;
                }

                if (segment.startSimulationTimeSeconds > currentTime)
                {
                    nextStart = Mathf.Min(nextStart, segment.startSimulationTimeSeconds);
                }
            }
        }

        float scenarioEnd = scenarioDefinition != null
            ? scenarioDefinition.durationSeconds
            : float.PositiveInfinity;

        float nextTime = Mathf.Min(nextStart, scenarioEnd);

        if (float.IsInfinity(nextTime))
        {
            return float.PositiveInfinity;
        }

        return Mathf.Max(0f, nextTime - currentTime);
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
        if (string.IsNullOrEmpty(scenePairName))
        {
            return false;
        }

        if (targetBinding != null)
        {
            bool matches = targetBinding.MatchesAccessPointTarget(canonicalTargetId, scenePairName);

            if (!matches &&
                logUnresolvedAccessPointEffects &&
                !string.IsNullOrEmpty(canonicalTargetId) &&
                !targetBinding.HasResolvedAccessPoint(canonicalTargetId) &&
                unresolvedEffectWarnings.Add("access:" + canonicalTargetId))
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

    private bool MatchesArea(string canonicalTargetId, string sceneAreaId)
    {
        if (targetBinding != null)
        {
            bool matches = targetBinding.MatchesAreaTarget(canonicalTargetId, sceneAreaId);

            if (!matches &&
                logUnresolvedAreaEffects &&
                !string.IsNullOrEmpty(canonicalTargetId) &&
                !targetBinding.HasResolvedArea(canonicalTargetId) &&
                unresolvedEffectWarnings.Add("area:" + canonicalTargetId))
            {
                runLogger?.LogWarning(
                    $"unresolvedAreaId={canonicalTargetId} " +
                    "ScenarioTargetBindingでsceneAreaIdを設定してください。"
                );
            }

            return matches;
        }

        return string.Equals(canonicalTargetId, sceneAreaId, StringComparison.Ordinal);
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
