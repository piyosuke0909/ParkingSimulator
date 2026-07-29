using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-500)]
public class P2SimulationEventPublisher : MonoBehaviour
{
    public static P2SimulationEventPublisher Instance { get; private set; }

    public event Action<P2SimulationEvent> EventPublished;

    [Header("Scenario Sources")]
    public ScenarioFactorRuntime scenarioRuntime;
    public SimulationClock simulationClock;
    public LocalScenarioDefinition scenarioDefinition;
    public ScenarioTargetBinding targetBinding;
    public VehicleSpawnManager vehicleSpawnManager;

    [Header("Behavior")]
    public bool autoFindReferences = true;
    public bool publishScenarioEvents = true;
    public bool publishScenarioFactorEvents = true;
    public bool publishVehicleStateEvents = true;

    [Min(0.02f)]
    public float vehicleStatePollIntervalSeconds = 0.1f;

    [Min(0.001f)]
    public float rewindDetectionToleranceSeconds = 0.01f;

    [Header("Numeric Output Precision")]
    [Range(0, 6)]
    public int timeDecimalPlaces = 3;

    [Range(0, 6)]
    public int scalarDecimalPlaces = 4;

    [Range(0, 6)]
    public int vectorDecimalPlaces = 3;

    [Header("Runtime (Read Only)")]
    [SerializeField]
    private string sessionId;

    [SerializeField]
    private string currentRunId;

    [SerializeField]
    private int runSequence;

    [SerializeField]
    private long eventSequenceNumber;

    [SerializeField]
    private bool scenarioCompletedPublished;

    private readonly HashSet<string> activeFactorKeys =
        new HashSet<string>(StringComparer.Ordinal);

    private readonly Dictionary<int, ObservedVehicleState> observedVehicles =
        new Dictionary<int, ObservedVehicleState>();

    private readonly HashSet<int> exitedVehicleInstanceIds = new HashSet<int>();

    private float previousSimulationTimeSeconds;
    private double previousTotalAdvancedSimulationSeconds;
    private float nextVehiclePollAt;
    private bool runStarted;
    private bool isQuitting;

    public string SessionId => sessionId;
    public string CurrentRunId => currentRunId;
    public int RunSequence => runSequence;
    public long EventSequenceNumber => eventSequenceNumber;

    private sealed class ObservedVehicleState
    {
        public NPC_CarController controller;
        public string movementState;
        public P2VehicleEventPayload lastPayload;
        public bool exitPublished;
        public bool seenThisPoll;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning(
                name + ": P2SimulationEventPublisher が複数存在します。" +
                "先に初期化されたInstanceを使用します。"
            );
        }
        else
        {
            Instance = this;
        }

        sessionId = BuildSessionId();
        ResolveReferences();
    }

    private void Start()
    {
        BeginNewRun("play_start");
    }

    private void Update()
    {
        ResolveReferences();

        float currentSimulationTime = GetSimulationTimeSeconds();
        double currentTotalAdvanced = simulationClock != null
            ? simulationClock.TotalAdvancedSimulationSeconds
            : (double)Time.time;

        bool timeMovedBackward =
            runStarted &&
            currentSimulationTime + rewindDetectionToleranceSeconds <
            previousSimulationTimeSeconds;

        if (timeMovedBackward)
        {
            bool loopWrapped =
                simulationClock != null &&
                simulationClock.loopAtScenarioEnd &&
                currentTotalAdvanced >= previousTotalAdvancedSimulationSeconds;

            if (loopWrapped && !scenarioCompletedPublished && publishScenarioEvents)
            {
                PublishScenarioCompleted("scenario_loop_completed");
            }

            BeginNewRun(loopWrapped ? "scenario_loop" : "simulation_time_rewind");
            return;
        }

        if (publishScenarioFactorEvents)
        {
            EvaluateScenarioFactorTransitions(currentSimulationTime, false);
        }

        if (publishVehicleStateEvents && Time.unscaledTime >= nextVehiclePollAt)
        {
            nextVehiclePollAt = Time.unscaledTime +
                                Mathf.Max(0.02f, vehicleStatePollIntervalSeconds);
            EvaluateVehicleTransitions();
        }

        if (publishScenarioEvents &&
            !scenarioCompletedPublished &&
            IsScenarioCompleted(currentSimulationTime))
        {
            PublishScenarioCompleted("duration_reached");
        }

        previousSimulationTimeSeconds = currentSimulationTime;
        previousTotalAdvancedSimulationSeconds = currentTotalAdvanced;
    }

    private void OnApplicationQuit()
    {
        isQuitting = true;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    [ContextMenu("Begin New Event Run")]
    public void BeginNewRunFromContextMenu()
    {
        BeginNewRun("manual");
    }

    [ContextMenu("Resolve Event References")]
    public void ResolveReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (scenarioRuntime == null)
        {
            scenarioRuntime = ScenarioFactorRuntime.Instance;
        }

        if (scenarioRuntime == null)
        {
#if UNITY_2023_1_OR_NEWER
            scenarioRuntime = FindFirstObjectByType<ScenarioFactorRuntime>();
#else
            scenarioRuntime = FindObjectOfType<ScenarioFactorRuntime>();
#endif
        }

        if (simulationClock == null && scenarioRuntime != null)
        {
            simulationClock = scenarioRuntime.Clock;
        }

        if (scenarioDefinition == null && scenarioRuntime != null)
        {
            scenarioDefinition = scenarioRuntime.Definition;
        }

        if (targetBinding == null && scenarioRuntime != null)
        {
            targetBinding = scenarioRuntime.targetBinding;
        }

        if (simulationClock == null)
        {
#if UNITY_2023_1_OR_NEWER
            simulationClock = FindFirstObjectByType<SimulationClock>();
#else
            simulationClock = FindObjectOfType<SimulationClock>();
#endif
        }

        if (scenarioDefinition == null && simulationClock != null)
        {
            scenarioDefinition = simulationClock.scenarioDefinition;
        }

        if (targetBinding == null)
        {
#if UNITY_2023_1_OR_NEWER
            targetBinding = FindFirstObjectByType<ScenarioTargetBinding>();
#else
            targetBinding = FindObjectOfType<ScenarioTargetBinding>();
#endif
        }

        if (vehicleSpawnManager == null)
        {
#if UNITY_2023_1_OR_NEWER
            vehicleSpawnManager = FindFirstObjectByType<VehicleSpawnManager>();
#else
            vehicleSpawnManager = FindObjectOfType<VehicleSpawnManager>();
#endif
        }
    }

    public void BeginNewRun(string startReason)
    {
        ResolveReferences();

        runSequence++;
        eventSequenceNumber = 0L;
        currentRunId = sessionId + "-run-" + runSequence.ToString("D4");
        scenarioCompletedPublished = false;
        activeFactorKeys.Clear();
        observedVehicles.Clear();
        exitedVehicleInstanceIds.Clear();
        runStarted = true;

        previousSimulationTimeSeconds = GetSimulationTimeSeconds();
        previousTotalAdvancedSimulationSeconds = simulationClock != null
            ? simulationClock.TotalAdvancedSimulationSeconds
            : (double)Time.time;
        nextVehiclePollAt = Time.unscaledTime;

        if (publishScenarioEvents)
        {
            P2ScenarioEventPayload payload = BuildScenarioPayload();
            payload.startReason = NullToEmpty(startReason);

            Publish(CreateEvent(
                P2EventContractConstants.ScenarioStarted,
                "scenario",
                GetScenarioId(),
                string.Empty,
                payload,
                null,
                null,
                null
            ));
        }

        if (publishScenarioFactorEvents)
        {
            EvaluateScenarioFactorTransitions(previousSimulationTimeSeconds, true);
        }

        CaptureVehicleBaseline();
    }

    public void PublishEntranceSelected(
        string correlationId,
        IList<string> canonicalIds,
        IList<string> sceneIds,
        IList<float> weights,
        string selectedCanonicalId,
        string selectedSceneId)
    {
        P2SelectionEventPayload payload = new P2SelectionEventPayload
        {
            selectionType = "entrance",
            selectedCanonicalId = NullToEmpty(selectedCanonicalId),
            selectedSceneId = NullToEmpty(selectedSceneId)
        };

        int count = Math.Min(
            canonicalIds != null ? canonicalIds.Count : 0,
            sceneIds != null ? sceneIds.Count : 0
        );

        for (int index = 0; index < count; index++)
        {
            float weight = weights != null && index < weights.Count
                ? weights[index]
                : 1f;

            payload.candidates.Add(new P2WeightedSelectionCandidate
            {
                canonicalId = NullToEmpty(canonicalIds[index]),
                sceneId = NullToEmpty(sceneIds[index]),
                weight = RoundScalar(SanitizeWeight(weight)),
                availableSlotCount = 0
            });
        }

        Publish(CreateEvent(
            P2EventContractConstants.EntranceSelected,
            "access_point",
            payload.selectedCanonicalId,
            correlationId,
            null,
            null,
            payload,
            null
        ));
    }

    public void PublishParkingAreaSelected(
        string correlationId,
        IList<string> canonicalIds,
        IList<string> sceneIds,
        IList<int> availableSlotCounts,
        IList<float> weights,
        string selectedCanonicalId,
        string selectedSceneId)
    {
        P2SelectionEventPayload payload = new P2SelectionEventPayload
        {
            selectionType = "parking_area",
            selectedCanonicalId = NullToEmpty(selectedCanonicalId),
            selectedSceneId = NullToEmpty(selectedSceneId)
        };

        int count = Math.Min(
            canonicalIds != null ? canonicalIds.Count : 0,
            sceneIds != null ? sceneIds.Count : 0
        );

        for (int index = 0; index < count; index++)
        {
            int slotCount = availableSlotCounts != null && index < availableSlotCounts.Count
                ? Mathf.Max(0, availableSlotCounts[index])
                : 0;
            float weight = weights != null && index < weights.Count
                ? weights[index]
                : 1f;

            payload.candidates.Add(new P2WeightedSelectionCandidate
            {
                canonicalId = NullToEmpty(canonicalIds[index]),
                sceneId = NullToEmpty(sceneIds[index]),
                weight = RoundScalar(SanitizeWeight(weight)),
                availableSlotCount = slotCount
            });

            if (string.Equals(
                    canonicalIds[index],
                    selectedCanonicalId,
                    StringComparison.Ordinal))
            {
                payload.selectedAvailableSlotCount = slotCount;
            }
        }

        Publish(CreateEvent(
            P2EventContractConstants.ParkingAreaSelected,
            "area",
            payload.selectedCanonicalId,
            correlationId,
            null,
            null,
            payload,
            null
        ));
    }

    public void PublishParkingSlotSelected(
        string correlationId,
        string slotId,
        string canonicalAreaId,
        string sceneAreaId)
    {
        P2SelectionEventPayload payload = new P2SelectionEventPayload
        {
            selectionType = "parking_slot",
            selectedCanonicalId = NullToEmpty(slotId),
            selectedSceneId = NullToEmpty(slotId),
            selectedParentCanonicalId = NullToEmpty(canonicalAreaId),
            selectedParentSceneId = NullToEmpty(sceneAreaId),
            selectedSlotId = NullToEmpty(slotId)
        };

        Publish(CreateEvent(
            P2EventContractConstants.ParkingSlotSelected,
            "parking_slot",
            payload.selectedCanonicalId,
            correlationId,
            null,
            null,
            payload,
            null
        ));
    }

    public void PublishVehicleSpawned(
        GameObject vehicleObject,
        int activeVehicleCount,
        int maxConcurrentVehicles,
        int totalSpawnedCount)
    {
        P2VehicleEventPayload vehiclePayload = BuildVehiclePayload(vehicleObject);
        vehiclePayload.activeVehicleCount = Mathf.Max(0, activeVehicleCount);
        vehiclePayload.maxConcurrentVehicles = Mathf.Max(0, maxConcurrentVehicles);
        vehiclePayload.totalSpawnedCount = Mathf.Max(0, totalSpawnedCount);
        vehiclePayload.reason = "spawn_success";

        TrackVehicle(vehicleObject);

        Publish(CreateEvent(
            P2EventContractConstants.VehicleSpawned,
            "vehicle",
            vehiclePayload.vehicleId,
            vehiclePayload.vehicleId,
            null,
            null,
            null,
            vehiclePayload
        ));
    }

    public void PublishVehicleExited(
        GameObject vehicleObject,
        int activeVehicleCount,
        int maxConcurrentVehicles,
        int totalSpawnedCount,
        string reason)
    {
        int instanceId = vehicleObject != null ? vehicleObject.GetInstanceID() : 0;

        if (instanceId != 0 && exitedVehicleInstanceIds.Contains(instanceId))
        {
            return;
        }

        P2VehicleEventPayload payload = null;
        string previousState = string.Empty;

        if (instanceId != 0 &&
            observedVehicles.TryGetValue(instanceId, out ObservedVehicleState observedState))
        {
            previousState = observedState.movementState ?? string.Empty;
            payload = CopyVehiclePayload(observedState.lastPayload);
        }

        if (payload == null)
        {
            payload = BuildVehiclePayload(vehicleObject);
        }

        if (vehicleObject != null)
        {
            P2VehicleEventPayload latestPayload = BuildVehiclePayload(vehicleObject);
            MergeLatestVehiclePayload(payload, latestPayload);
        }

        payload.previousMovementState = previousState;
        payload.movementState = NPC_CarMoveState.Finished.ToString();
        payload.reason = NullToEmpty(reason);
        payload.activeVehicleCount = Mathf.Max(0, activeVehicleCount);
        payload.maxConcurrentVehicles = Mathf.Max(0, maxConcurrentVehicles);
        payload.totalSpawnedCount = Mathf.Max(0, totalSpawnedCount);

        if (instanceId != 0)
        {
            exitedVehicleInstanceIds.Add(instanceId);
            observedVehicles.Remove(instanceId);
        }

        Publish(CreateEvent(
            P2EventContractConstants.VehicleExited,
            "vehicle",
            payload.vehicleId,
            payload.vehicleId,
            null,
            null,
            null,
            payload
        ));
    }

    private void EvaluateScenarioFactorTransitions(
        float simulationTimeSeconds,
        bool publishCurrentAsActivated)
    {
        LocalScenarioDefinition definition = GetScenarioDefinition();
        HashSet<string> currentKeys = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, ScenarioFactorDefinition> currentFactors =
            new Dictionary<string, ScenarioFactorDefinition>(StringComparer.Ordinal);

        if (definition != null && definition.scenarioFactors != null)
        {
            foreach (ScenarioFactorDefinition factor in definition.scenarioFactors)
            {
                if (factor == null || !factor.IsActiveAt(simulationTimeSeconds))
                {
                    continue;
                }

                string key = GetFactorKey(factor);
                currentKeys.Add(key);
                currentFactors[key] = factor;
            }
        }

        foreach (string key in currentKeys)
        {
            if (!publishCurrentAsActivated && activeFactorKeys.Contains(key))
            {
                continue;
            }

            if (currentFactors.TryGetValue(key, out ScenarioFactorDefinition factor))
            {
                PublishScenarioFactorEvent(
                    P2EventContractConstants.ScenarioFactorActivated,
                    factor
                );
            }
        }

        if (!publishCurrentAsActivated)
        {
            foreach (string previousKey in new List<string>(activeFactorKeys))
            {
                if (currentKeys.Contains(previousKey))
                {
                    continue;
                }

                ScenarioFactorDefinition previousFactor = FindFactorByKey(previousKey);

                if (previousFactor != null)
                {
                    PublishScenarioFactorEvent(
                        P2EventContractConstants.ScenarioFactorDeactivated,
                        previousFactor
                    );
                }
            }
        }

        activeFactorKeys.Clear();

        foreach (string key in currentKeys)
        {
            activeFactorKeys.Add(key);
        }
    }

    private void PublishScenarioFactorEvent(
        string eventType,
        ScenarioFactorDefinition factor)
    {
        if (factor == null)
        {
            return;
        }

        P2ScenarioFactorEventPayload payload = BuildScenarioFactorPayload(factor);

        Publish(CreateEvent(
            eventType,
            "scenario_factor",
            payload.scenarioFactorId,
            string.Empty,
            null,
            payload,
            null,
            null
        ));
    }

    private void PublishScenarioCompleted(string completionReason)
    {
        if (scenarioCompletedPublished)
        {
            return;
        }

        P2ScenarioEventPayload payload = BuildScenarioPayload();
        payload.completionReason = NullToEmpty(completionReason);

        Publish(CreateEvent(
            P2EventContractConstants.ScenarioCompleted,
            "scenario",
            GetScenarioId(),
            string.Empty,
            payload,
            null,
            null,
            null
        ));

        scenarioCompletedPublished = true;
    }

    private void CaptureVehicleBaseline()
    {
        observedVehicles.Clear();

        foreach (NPC_CarController controller in FindVehiclesInActiveScene())
        {
            TrackVehicle(controller != null ? controller.gameObject : null);
        }
    }

    private void EvaluateVehicleTransitions()
    {
        foreach (ObservedVehicleState state in observedVehicles.Values)
        {
            state.seenThisPoll = false;
        }

        foreach (NPC_CarController controller in FindVehiclesInActiveScene())
        {
            if (controller == null)
            {
                continue;
            }

            int instanceId = controller.gameObject.GetInstanceID();

            if (exitedVehicleInstanceIds.Contains(instanceId))
            {
                continue;
            }

            if (!observedVehicles.TryGetValue(instanceId, out ObservedVehicleState state))
            {
                state = new ObservedVehicleState
                {
                    controller = controller,
                    movementState = controller.moveState.ToString(),
                    lastPayload = BuildVehiclePayload(controller.gameObject),
                    seenThisPoll = true
                };
                observedVehicles.Add(instanceId, state);
                continue;
            }

            state.seenThisPoll = true;
            string previousState = state.movementState ?? string.Empty;
            string currentState = controller.moveState.ToString();
            P2VehicleEventPayload currentPayload = BuildVehiclePayload(controller.gameObject);
            currentPayload.previousMovementState = previousState;

            if (!string.Equals(previousState, currentState, StringComparison.Ordinal))
            {
                if (currentState == NPC_CarMoveState.Parked.ToString())
                {
                    currentPayload.reason = "parking_completed";
                    PublishVehicleStateEvent(
                        P2EventContractConstants.VehicleParked,
                        currentPayload
                    );
                }
                else if (IsLeavingState(currentState) && !IsLeavingState(previousState))
                {
                    currentPayload.reason = "leaving_started";
                    PublishVehicleStateEvent(
                        P2EventContractConstants.VehicleLeaving,
                        currentPayload
                    );
                }
            }

            state.controller = controller;
            state.movementState = currentState;
            state.lastPayload = currentPayload;
        }

        if (isQuitting)
        {
            return;
        }

        List<int> removedIds = new List<int>();

        foreach (KeyValuePair<int, ObservedVehicleState> pair in observedVehicles)
        {
            ObservedVehicleState state = pair.Value;

            if (state.seenThisPoll)
            {
                continue;
            }

            if (!state.exitPublished && state.lastPayload != null)
            {
                state.lastPayload.previousMovementState = state.movementState;
                state.lastPayload.movementState = "Destroyed";
                state.lastPayload.reason = "vehicle_destroyed";
                PublishVehicleStateEvent(
                    P2EventContractConstants.VehicleExited,
                    state.lastPayload
                );
                exitedVehicleInstanceIds.Add(pair.Key);
            }

            removedIds.Add(pair.Key);
        }

        foreach (int removedId in removedIds)
        {
            observedVehicles.Remove(removedId);
        }
    }

    private void PublishVehicleStateEvent(
        string eventType,
        P2VehicleEventPayload vehiclePayload)
    {
        if (vehiclePayload == null)
        {
            return;
        }

        if (vehicleSpawnManager != null)
        {
            vehiclePayload.activeVehicleCount = vehicleSpawnManager.ActiveVehicleCount;
            vehiclePayload.maxConcurrentVehicles = vehicleSpawnManager.maxConcurrentVehicles;
            vehiclePayload.totalSpawnedCount = vehicleSpawnManager.TotalSpawnedCount;
        }

        Publish(CreateEvent(
            eventType,
            "vehicle",
            vehiclePayload.vehicleId,
            vehiclePayload.vehicleId,
            null,
            null,
            null,
            vehiclePayload
        ));
    }

    private void TrackVehicle(GameObject vehicleObject)
    {
        if (vehicleObject == null)
        {
            return;
        }

        NPC_CarController controller = vehicleObject.GetComponent<NPC_CarController>();

        if (controller == null)
        {
            return;
        }

        int instanceId = vehicleObject.GetInstanceID();

        if (exitedVehicleInstanceIds.Contains(instanceId))
        {
            return;
        }

        observedVehicles[instanceId] = new ObservedVehicleState
        {
            controller = controller,
            movementState = controller.moveState.ToString(),
            lastPayload = BuildVehiclePayload(vehicleObject),
            seenThisPoll = true
        };
    }

    private P2SimulationEvent CreateEvent(
        string eventType,
        string entityType,
        string entityId,
        string correlationId,
        P2ScenarioEventPayload scenarioPayload,
        P2ScenarioFactorEventPayload factorPayload,
        P2SelectionEventPayload selectionPayload,
        P2VehicleEventPayload vehiclePayload)
    {
        eventSequenceNumber++;

        P2SimulationEvent simulationEvent = new P2SimulationEvent
        {
            eventId = currentRunId + "-event-" + eventSequenceNumber.ToString("D10"),
            eventType = NullToEmpty(eventType),
            generatedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
            sceneName = SceneManager.GetActiveScene().name,
            sessionId = sessionId,
            runId = currentRunId,
            sequenceNumber = eventSequenceNumber,
            scenarioId = GetScenarioId(),
            scenarioVersion = GetScenarioVersion(),
            facilityId = GetFacilityId(),
            simulationTimeSeconds = RoundTime(GetSimulationTimeSeconds()),
            correlationId = NullToEmpty(correlationId),
            entityType = NullToEmpty(entityType),
            entityId = NullToEmpty(entityId),
            payload = new P2EventPayload
            {
                scenario = scenarioPayload,
                scenarioFactor = factorPayload,
                selection = selectionPayload,
                vehicle = vehiclePayload
            }
        };

        return simulationEvent;
    }

    private void Publish(P2SimulationEvent simulationEvent)
    {
        EventPublished?.Invoke(simulationEvent);
    }

    private P2ScenarioEventPayload BuildScenarioPayload()
    {
        LocalScenarioDefinition definition = GetScenarioDefinition();

        P2ScenarioEventPayload payload = new P2ScenarioEventPayload
        {
            activeVehicleCount = vehicleSpawnManager != null
                ? vehicleSpawnManager.ActiveVehicleCount
                : 0,
            totalSpawnedCount = vehicleSpawnManager != null
                ? vehicleSpawnManager.TotalSpawnedCount
                : 0
        };

        if (definition == null)
        {
            return payload;
        }

        payload.scenarioName = NullToEmpty(definition.scenarioName);
        payload.mapVersion = NullToEmpty(definition.mapVersion);
        payload.timeOfDay = NullToEmpty(definition.timeOfDay);
        payload.randomSeed = scenarioRuntime != null && scenarioRuntime.RandomService != null
            ? scenarioRuntime.RandomService.CurrentSeed
            : definition.randomSeed;
        payload.randomSeedPolicy = NullToEmpty(definition.randomSeedPolicy);
        payload.durationSeconds = RoundTime(definition.durationSeconds);
        return payload;
    }

    private P2ScenarioFactorEventPayload BuildScenarioFactorPayload(
        ScenarioFactorDefinition factor)
    {
        P2ScenarioFactorEventPayload payload = new P2ScenarioFactorEventPayload
        {
            scenarioFactorId = NullToEmpty(factor.scenarioFactorId),
            factorCode = NullToEmpty(factor.factorCode),
            label = NullToEmpty(factor.label),
            category = factor.category.ToString(),
            informationAvailability = factor.informationAvailability.ToString(),
            intensity = RoundScalar(factor.intensity),
            startSimulationTimeSeconds = RoundTime(factor.startSimulationTimeSeconds),
            endSimulationTimeSeconds = RoundTime(factor.endSimulationTimeSeconds),
            hasKnownFromSimulationTimeSeconds = factor.hasKnownFromSimulationTimeSeconds,
            knownFromSimulationTimeSeconds = RoundTime(
                factor.knownFromSimulationTimeSeconds
            )
        };

        if (factor.effects == null)
        {
            return payload;
        }

        foreach (ScenarioFactorEffect effect in factor.effects)
        {
            if (effect == null || !effect.enabled)
            {
                continue;
            }

            payload.effects.Add(new P2EventFactorEffect
            {
                scenarioFactorEffectId = NullToEmpty(effect.scenarioFactorEffectId),
                targetType = effect.targetType.ToString(),
                targetId = NullToEmpty(effect.targetId),
                resolvedTargetId = ResolveEffectTargetId(
                    effect.targetType,
                    effect.targetId
                ),
                metric = NullToEmpty(effect.metric),
                operation = effect.operation.ToString(),
                value = RoundScalar(effect.value),
                unit = NullToEmpty(effect.unit)
            });
        }

        payload.effects.Sort((left, right) =>
            string.CompareOrdinal(
                left.scenarioFactorEffectId,
                right.scenarioFactorEffectId
            ));
        return payload;
    }

    private P2VehicleEventPayload BuildVehiclePayload(GameObject vehicleObject)
    {
        P2VehicleEventPayload payload = new P2VehicleEventPayload();

        if (vehicleObject == null)
        {
            payload.vehicleId = "vehicle-unknown";
            return payload;
        }

        Car car = vehicleObject.GetComponent<Car>();
        NPC_CarController controller = vehicleObject.GetComponent<NPC_CarController>();
        P2VehicleSnapshotMetadata metadata =
            vehicleObject.GetComponent<P2VehicleSnapshotMetadata>();

        payload.vehicleId = car != null && !string.IsNullOrWhiteSpace(car.carId)
            ? car.carId
            : vehicleObject.name;
        payload.objectName = vehicleObject.name;
        payload.currentSlotId = car != null ? NullToEmpty(car.GetCurrentSlotId()) : string.Empty;
        payload.movementState = controller != null
            ? controller.moveState.ToString()
            : "Unknown";
        payload.position = ToVector(vehicleObject.transform.position);

        if (metadata != null)
        {
            payload.spawnSequenceNumber = metadata.spawnSequenceNumber;
            payload.spawnedAtSimulationTimeSeconds = RoundTime(
                metadata.spawnedAtSimulationTimeSeconds
            );
            payload.entranceAccessPointId = NullToEmpty(
                metadata.entranceAccessPointId
            );
            payload.entranceScenePairName = NullToEmpty(
                metadata.entranceScenePairName
            );
            payload.targetAreaId = NullToEmpty(metadata.targetAreaId);
            payload.targetSceneAreaId = NullToEmpty(metadata.targetSceneAreaId);
            payload.targetSlotId = NullToEmpty(metadata.targetSlotId);
        }

        return payload;
    }

    private static P2VehicleEventPayload CopyVehiclePayload(P2VehicleEventPayload source)
    {
        if (source == null)
        {
            return null;
        }

        return new P2VehicleEventPayload
        {
            vehicleId = source.vehicleId,
            objectName = source.objectName,
            spawnSequenceNumber = source.spawnSequenceNumber,
            spawnedAtSimulationTimeSeconds = source.spawnedAtSimulationTimeSeconds,
            entranceAccessPointId = source.entranceAccessPointId,
            entranceScenePairName = source.entranceScenePairName,
            targetAreaId = source.targetAreaId,
            targetSceneAreaId = source.targetSceneAreaId,
            targetSlotId = source.targetSlotId,
            currentSlotId = source.currentSlotId,
            movementState = source.movementState,
            previousMovementState = source.previousMovementState,
            reason = source.reason,
            activeVehicleCount = source.activeVehicleCount,
            maxConcurrentVehicles = source.maxConcurrentVehicles,
            totalSpawnedCount = source.totalSpawnedCount,
            position = source.position != null
                ? new P2Vector3Snapshot
                {
                    x = source.position.x,
                    y = source.position.y,
                    z = source.position.z
                }
                : new P2Vector3Snapshot()
        };
    }

    private static void MergeLatestVehiclePayload(
        P2VehicleEventPayload target,
        P2VehicleEventPayload latest)
    {
        if (target == null || latest == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(latest.vehicleId))
        {
            target.vehicleId = latest.vehicleId;
        }

        if (!string.IsNullOrWhiteSpace(latest.objectName))
        {
            target.objectName = latest.objectName;
        }

        if (latest.spawnSequenceNumber > 0)
        {
            target.spawnSequenceNumber = latest.spawnSequenceNumber;
        }

        if (latest.spawnedAtSimulationTimeSeconds > 0d)
        {
            target.spawnedAtSimulationTimeSeconds = latest.spawnedAtSimulationTimeSeconds;
        }

        if (!string.IsNullOrWhiteSpace(latest.entranceAccessPointId))
        {
            target.entranceAccessPointId = latest.entranceAccessPointId;
        }

        if (!string.IsNullOrWhiteSpace(latest.entranceScenePairName))
        {
            target.entranceScenePairName = latest.entranceScenePairName;
        }

        if (!string.IsNullOrWhiteSpace(latest.targetAreaId))
        {
            target.targetAreaId = latest.targetAreaId;
        }

        if (!string.IsNullOrWhiteSpace(latest.targetSceneAreaId))
        {
            target.targetSceneAreaId = latest.targetSceneAreaId;
        }

        if (!string.IsNullOrWhiteSpace(latest.targetSlotId))
        {
            target.targetSlotId = latest.targetSlotId;
        }

        target.currentSlotId = latest.currentSlotId;

        if (latest.position != null)
        {
            target.position = latest.position;
        }
    }

    private List<NPC_CarController> FindVehiclesInActiveScene()
    {
        NPC_CarController[] allControllers;

#if UNITY_2023_1_OR_NEWER
        allControllers = FindObjectsByType<NPC_CarController>(FindObjectsSortMode.None);
#else
        allControllers = FindObjectsOfType<NPC_CarController>();
#endif

        Scene activeScene = SceneManager.GetActiveScene();
        List<NPC_CarController> result = new List<NPC_CarController>();

        foreach (NPC_CarController controller in allControllers)
        {
            if (controller == null || controller.gameObject.scene != activeScene)
            {
                continue;
            }

            result.Add(controller);
        }

        return result;
    }

    private ScenarioFactorDefinition FindFactorByKey(string key)
    {
        LocalScenarioDefinition definition = GetScenarioDefinition();

        if (definition == null || definition.scenarioFactors == null)
        {
            return null;
        }

        foreach (ScenarioFactorDefinition factor in definition.scenarioFactors)
        {
            if (factor != null &&
                string.Equals(GetFactorKey(factor), key, StringComparison.Ordinal))
            {
                return factor;
            }
        }

        return null;
    }

    private static string GetFactorKey(ScenarioFactorDefinition factor)
    {
        if (factor == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(factor.scenarioFactorId))
        {
            return factor.scenarioFactorId;
        }

        return !string.IsNullOrWhiteSpace(factor.factorCode)
            ? factor.factorCode
            : factor.GetHashCode().ToString();
    }

    private bool IsScenarioCompleted(float simulationTimeSeconds)
    {
        LocalScenarioDefinition definition = GetScenarioDefinition();
        return definition != null &&
               simulationTimeSeconds >= definition.durationSeconds;
    }

    private LocalScenarioDefinition GetScenarioDefinition()
    {
        if (scenarioDefinition != null)
        {
            return scenarioDefinition;
        }

        if (scenarioRuntime != null && scenarioRuntime.Definition != null)
        {
            return scenarioRuntime.Definition;
        }

        return simulationClock != null ? simulationClock.scenarioDefinition : null;
    }

    private float GetSimulationTimeSeconds()
    {
        if (simulationClock != null)
        {
            return Mathf.Max(0f, simulationClock.SimulationTimeSeconds);
        }

        if (scenarioRuntime != null)
        {
            return Mathf.Max(0f, scenarioRuntime.SimulationTimeSeconds);
        }

        return Mathf.Max(0f, Time.time);
    }

    private string GetScenarioId()
    {
        LocalScenarioDefinition definition = GetScenarioDefinition();
        return definition != null && !string.IsNullOrWhiteSpace(definition.scenarioId)
            ? definition.scenarioId
            : "scenario-unconfigured";
    }

    private string GetScenarioVersion()
    {
        LocalScenarioDefinition definition = GetScenarioDefinition();
        return definition != null ? NullToEmpty(definition.scenarioVersion) : string.Empty;
    }

    private string GetFacilityId()
    {
        LocalScenarioDefinition definition = GetScenarioDefinition();
        return definition != null && !string.IsNullOrWhiteSpace(definition.facilityId)
            ? definition.facilityId
            : "facility-unconfigured";
    }

    private string ResolveEffectTargetId(
        FactorEffectTargetType targetType,
        string targetId)
    {
        string originalTargetId = NullToEmpty(targetId);

        if (string.IsNullOrWhiteSpace(originalTargetId) || targetBinding == null)
        {
            return originalTargetId;
        }

        if (targetType == FactorEffectTargetType.AccessPoint &&
            targetBinding.accessPointBindings != null)
        {
            foreach (ScenarioAccessPointBinding binding in targetBinding.accessPointBindings)
            {
                if (binding == null ||
                    !string.Equals(
                        binding.canonicalAccessPointId,
                        originalTargetId,
                        StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(binding.scenePairName))
                {
                    continue;
                }

                return scenarioRuntime != null
                    ? scenarioRuntime.GetCanonicalAccessPointId(binding.scenePairName)
                    : targetBinding.GetPrimaryCanonicalAccessPointId(binding.scenePairName);
            }
        }
        else if (targetType == FactorEffectTargetType.Area &&
                 targetBinding.areaBindings != null)
        {
            foreach (ScenarioAreaBinding binding in targetBinding.areaBindings)
            {
                if (binding == null ||
                    !string.Equals(
                        binding.canonicalAreaId,
                        originalTargetId,
                        StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(binding.sceneAreaId))
                {
                    continue;
                }

                return scenarioRuntime != null
                    ? scenarioRuntime.GetCanonicalAreaId(binding.sceneAreaId)
                    : targetBinding.GetCanonicalAreaId(binding.sceneAreaId);
            }
        }

        return originalTargetId;
    }

    private static bool IsLeavingState(string movementState)
    {
        return movementState == NPC_CarMoveState.WaitingToBackOut.ToString() ||
               movementState == NPC_CarMoveState.BackingOut.ToString() ||
               movementState == NPC_CarMoveState.Leaving.ToString();
    }

    private P2Vector3Snapshot ToVector(Vector3 value)
    {
        return new P2Vector3Snapshot
        {
            x = RoundVector(value.x),
            y = RoundVector(value.y),
            z = RoundVector(value.z)
        };
    }

    private double RoundTime(float value)
    {
        return RoundNumber(value, timeDecimalPlaces);
    }

    private double RoundScalar(float value)
    {
        return RoundNumber(value, scalarDecimalPlaces);
    }

    private double RoundVector(float value)
    {
        return RoundNumber(value, vectorDecimalPlaces);
    }

    private static double RoundNumber(float value, int decimalPlaces)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0d;
        }

        int digits = Mathf.Clamp(decimalPlaces, 0, 6);
        double rounded = Math.Round(
            (double)value,
            digits,
            MidpointRounding.AwayFromZero
        );

        return Math.Abs(rounded) < Math.Pow(10d, -digits) * 0.5d
            ? 0d
            : rounded;
    }

    private static float SanitizeWeight(float weight)
    {
        return float.IsNaN(weight) || float.IsInfinity(weight) || weight < 0f
            ? 0f
            : weight;
    }

    private string BuildSessionId()
    {
        return "unity-session-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
    }

    private static string NullToEmpty(string value)
    {
        return value ?? string.Empty;
    }
}
