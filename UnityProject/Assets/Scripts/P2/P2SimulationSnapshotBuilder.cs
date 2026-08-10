using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class P2SimulationSnapshotBuilder : MonoBehaviour
{
    [Header("Scenario Sources")]
    public ScenarioFactorRuntime scenarioRuntime;
    public SimulationClock simulationClock;
    public LocalScenarioDefinition scenarioDefinition;
    public ScenarioTargetBinding targetBinding;

    [Header("Parking Sources")]
    public ParkingLotManager parkingLotManager;
    public VehicleSpawnManager vehicleSpawnManager;

    [Header("P3 Policy Source (Optional)")]
    public P3AreaPolicyRuntime p3AreaPolicyRuntime;

    [Header("Build Options")]
    public bool autoFindReferences = true;
    public bool includeParkingSlots = true;
    public bool includeVehicles = true;
    public bool includeAccessPoints = true;
    public bool includeScenarioFactorEffects = true;

    [Header("Numeric Output Precision")]
    [Range(0, 6)]
    public int timeDecimalPlaces = 3;

    [Range(0, 6)]
    public int scalarDecimalPlaces = 4;

    [Range(0, 6)]
    public int vectorDecimalPlaces = 3;

    private void Awake()
    {
        ResolveReferences();
    }

    [ContextMenu("Resolve Snapshot References")]
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

        if (parkingLotManager == null)
        {
#if UNITY_2023_1_OR_NEWER
            parkingLotManager = FindFirstObjectByType<ParkingLotManager>();
#else
            parkingLotManager = FindObjectOfType<ParkingLotManager>();
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

        if (p3AreaPolicyRuntime == null)
        {
            p3AreaPolicyRuntime = P3AreaPolicyRuntime.Instance;
        }

        if (p3AreaPolicyRuntime == null)
        {
#if UNITY_2023_1_OR_NEWER
            p3AreaPolicyRuntime = FindFirstObjectByType<P3AreaPolicyRuntime>();
#else
            p3AreaPolicyRuntime = FindObjectOfType<P3AreaPolicyRuntime>();
#endif
        }
    }

    public P2SimulationSnapshot BuildSnapshot(long sequenceNumber)
    {
        ResolveReferences();

        float simulationTimeSeconds = GetSimulationTimeSeconds();
        LocalScenarioDefinition definition = GetScenarioDefinition();
        P2SimulationRunIdentity runIdentity = P2SimulationRunContext.EnsureCurrentRun();

        P2SimulationSnapshot snapshot = new P2SimulationSnapshot
        {
            sequenceNumber = Math.Max(1L, sequenceNumber),
            generatedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
            sceneName = SceneManager.GetActiveScene().name,
            sessionId = runIdentity.sessionId,
            runId = runIdentity.runId
        };

        snapshot.snapshotId = BuildSnapshotId(
            definition,
            snapshot.runId,
            snapshot.sequenceNumber
        );
        snapshot.scenario = BuildScenarioSnapshot(definition, simulationTimeSeconds);
        snapshot.runtime = BuildRuntimeSnapshot(simulationTimeSeconds);
        snapshot.activeScenarioFactors = BuildActiveScenarioFactors(definition, simulationTimeSeconds);
        snapshot.accessPoints = includeAccessPoints
            ? BuildAccessPoints()
            : new List<P2AccessPointSnapshot>();
        snapshot.parkingSlots = includeParkingSlots
            ? BuildParkingSlots()
            : new List<P2ParkingSlotSnapshot>();
        snapshot.areas = BuildAreas();
        snapshot.vehicles = includeVehicles
            ? BuildVehicles()
            : new List<P2VehicleSnapshot>();
        snapshot.summary = BuildSummary(snapshot);

        return snapshot;
    }

    private P2ScenarioSnapshot BuildScenarioSnapshot(
        LocalScenarioDefinition definition,
        float simulationTimeSeconds)
    {
        P2ScenarioSnapshot result = new P2ScenarioSnapshot
        {
            simulationTimeSeconds = RoundTime(simulationTimeSeconds),
            isCompleted = scenarioRuntime != null && scenarioRuntime.IsScenarioCompleted
        };

        if (definition == null)
        {
            result.scenarioId = "scenario-unconfigured";
            result.facilityId = "facility-unconfigured";
            return result;
        }

        result.scenarioId = NullToEmpty(definition.scenarioId);
        result.scenarioVersion = NullToEmpty(definition.scenarioVersion);
        result.scenarioName = NullToEmpty(definition.scenarioName);
        result.facilityId = NullToEmpty(definition.facilityId);
        result.mapVersion = NullToEmpty(definition.mapVersion);
        result.timeOfDay = NullToEmpty(definition.timeOfDay);
        result.randomSeed = definition.randomSeed;
        result.randomSeedPolicy = NullToEmpty(definition.randomSeedPolicy);
        result.durationSeconds = RoundTime(Mathf.Max(0f, definition.durationSeconds));
        result.isCompleted = simulationTimeSeconds >= result.durationSeconds;

        return result;
    }

    private P2RuntimeSnapshot BuildRuntimeSnapshot(float simulationTimeSeconds)
    {
        float fallbackSpawnInterval = vehicleSpawnManager != null
            ? Mathf.Max(0.01f, vehicleSpawnManager.spawnInterval)
            : 5f;

        P2RuntimeSnapshot result = new P2RuntimeSnapshot();

        if (scenarioRuntime != null)
        {
            result.arrivalRateSourceId = NullToEmpty(scenarioRuntime.GetArrivalRateSourceId());
            result.arrivalDistribution = scenarioRuntime.GetArrivalDistribution().ToString().ToLowerInvariant();
            result.baseVehiclesPerMinute = RoundScalar(scenarioRuntime.GetBaseVehiclesPerMinute(fallbackSpawnInterval));
            result.arrivalRateMultiplier = RoundScalar(scenarioRuntime.GetArrivalRateMultiplier());
            result.effectiveVehiclesPerMinute = RoundScalar(scenarioRuntime.GetEffectiveVehiclesPerMinute(fallbackSpawnInterval));
            result.vehicleSpeedMultiplier = RoundScalar(scenarioRuntime.GetVehicleSpeedMultiplier());
        }
        else
        {
            result.arrivalRateSourceId = "default";
            result.arrivalDistribution = ArrivalDistribution.Fixed.ToString().ToLowerInvariant();
            result.baseVehiclesPerMinute = RoundScalar(60f / fallbackSpawnInterval);
            result.effectiveVehiclesPerMinute = result.baseVehiclesPerMinute;
        }

        if (vehicleSpawnManager != null)
        {
            result.activeVehicleCount = vehicleSpawnManager.ActiveVehicleCount;
            result.maxConcurrentVehicles = vehicleSpawnManager.maxConcurrentVehicles;
            result.totalSpawnedCount = vehicleSpawnManager.TotalSpawnedCount;
        }

        result.discoveredVehicleCount = FindCarsInActiveScene().Count;
        return result;
    }

    private List<P2ScenarioFactorSnapshot> BuildActiveScenarioFactors(
        LocalScenarioDefinition definition,
        float simulationTimeSeconds)
    {
        List<P2ScenarioFactorSnapshot> result = new List<P2ScenarioFactorSnapshot>();

        if (definition == null || definition.scenarioFactors == null)
        {
            return result;
        }

        foreach (ScenarioFactorDefinition factor in definition.scenarioFactors)
        {
            if (factor == null || !factor.IsActiveAt(simulationTimeSeconds))
            {
                continue;
            }

            P2ScenarioFactorSnapshot item = new P2ScenarioFactorSnapshot
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
                knownFromSimulationTimeSeconds = RoundTime(factor.knownFromSimulationTimeSeconds)
            };

            if (includeScenarioFactorEffects && factor.effects != null)
            {
                foreach (ScenarioFactorEffect effect in factor.effects)
                {
                    if (effect == null || !effect.enabled)
                    {
                        continue;
                    }

                    item.effects.Add(new P2ScenarioFactorEffectSnapshot
                    {
                        scenarioFactorEffectId = NullToEmpty(effect.scenarioFactorEffectId),
                        targetType = effect.targetType.ToString(),
                        targetId = NullToEmpty(effect.targetId),
                        resolvedTargetId = ResolveEffectTargetId(effect.targetType, effect.targetId),
                        metric = NullToEmpty(effect.metric),
                        operation = effect.operation.ToString(),
                        value = RoundScalar(effect.value),
                        unit = NullToEmpty(effect.unit)
                    });
                }
            }

            item.effects.Sort((left, right) =>
                string.CompareOrdinal(left.scenarioFactorEffectId, right.scenarioFactorEffectId));
            result.Add(item);
        }

        result.Sort((left, right) =>
            string.CompareOrdinal(left.scenarioFactorId, right.scenarioFactorId));
        return result;
    }

    private List<P2AccessPointSnapshot> BuildAccessPoints()
    {
        List<P2AccessPointSnapshot> result = new List<P2AccessPointSnapshot>();

        if (vehicleSpawnManager == null || vehicleSpawnManager.entranceExitPairs == null)
        {
            return result;
        }

        foreach (EntranceExitPair pair in vehicleSpawnManager.entranceExitPairs)
        {
            if (pair == null)
            {
                continue;
            }

            result.Add(new P2AccessPointSnapshot
            {
                accessPointId = GetCanonicalAccessPointId(pair.pairName),
                scenePairName = NullToEmpty(pair.pairName),
                entranceWaypointId = GetWaypointId(pair.entranceWaypoint),
                exitWaypointId = GetWaypointId(pair.exitWaypoint),
                currentPreferenceWeight = RoundScalar(scenarioRuntime != null
                    ? scenarioRuntime.GetAccessPointPreferenceWeight(pair.pairName)
                    : 1f),
                spawnPosition = ToVector(pair.spawnPoint != null
                    ? pair.spawnPoint.position
                    : Vector3.zero)
            });
        }

        result.Sort((left, right) =>
            string.CompareOrdinal(left.accessPointId, right.accessPointId));
        return result;
    }

    private float GetEffectiveAreaPreferenceWeight(string sceneAreaId)
    {
        float weight = scenarioRuntime != null
            ? scenarioRuntime.GetAreaPreferenceWeight(sceneAreaId)
            : 1f;

        if (p3AreaPolicyRuntime != null)
        {
            weight *= p3AreaPolicyRuntime.GetSelectionWeightMultiplier(sceneAreaId);
        }

        return Mathf.Max(0f, weight);
    }

    private List<P2ParkingSlotSnapshot> BuildParkingSlots()
    {
        List<P2ParkingSlotSnapshot> result = new List<P2ParkingSlotSnapshot>();

        if (parkingLotManager == null || parkingLotManager.allSlots == null)
        {
            return result;
        }

        foreach (ParkingSlot slot in parkingLotManager.allSlots)
        {
            if (slot == null)
            {
                continue;
            }

            Transform parkingPoint = slot.parkingPoint != null
                ? slot.parkingPoint
                : slot.transform;

            result.Add(new P2ParkingSlotSnapshot
            {
                slotId = NullToEmpty(slot.slotId),
                areaId = GetCanonicalAreaId(slot.areaId),
                sceneAreaId = NullToEmpty(slot.areaId),
                state = GetSnapshotSlotState(slot).ToString(),
                isAvailable = slot.IsAvailable(),
                sensorOccupied = slot.sensorOccupied,
                isLeaving = slot.isLeaving,
                reservedByVehicleId = GetVehicleId(slot.reservedBy),
                occupiedByVehicleId = GetVehicleId(slot.occupiedBy),
                accessWaypointId = GetWaypointId(slot.accessWaypoint),
                position = ToVector(slot.transform.position),
                parkingPointPosition = ToVector(parkingPoint.position)
            });
        }

        result.Sort((left, right) =>
            string.CompareOrdinal(left.slotId, right.slotId));
        return result;
    }

    private List<P2AreaSnapshot> BuildAreas()
    {
        Dictionary<string, P2AreaSnapshot> bySceneAreaId =
            new Dictionary<string, P2AreaSnapshot>(StringComparer.Ordinal);

        if (parkingLotManager != null && parkingLotManager.allSlots != null)
        {
            foreach (ParkingSlot slot in parkingLotManager.allSlots)
            {
                if (slot == null)
                {
                    continue;
                }

                string sceneAreaId = NullToEmpty(slot.areaId);

                if (!bySceneAreaId.TryGetValue(sceneAreaId, out P2AreaSnapshot area))
                {
                    area = new P2AreaSnapshot
                    {
                        sceneAreaId = sceneAreaId,
                        areaId = GetCanonicalAreaId(sceneAreaId),
                        currentPreferenceWeight = RoundScalar(GetEffectiveAreaPreferenceWeight(sceneAreaId))
                    };
                    bySceneAreaId.Add(sceneAreaId, area);
                }

                area.totalSlotCount++;

                if (slot.IsAvailable())
                {
                    area.availableSlotCount++;
                }

                switch (GetSnapshotSlotState(slot))
                {
                    case ParkingSlotState.Empty:
                        area.emptySlotCount++;
                        break;
                    case ParkingSlotState.Reserved:
                        area.reservedSlotCount++;
                        break;
                    case ParkingSlotState.Occupied:
                        area.occupiedSlotCount++;
                        break;
                    case ParkingSlotState.Leaving:
                        area.leavingSlotCount++;
                        break;
                    case ParkingSlotState.Disabled:
                        area.disabledSlotCount++;
                        break;
                }
            }
        }

        List<P2AreaSnapshot> result = new List<P2AreaSnapshot>(bySceneAreaId.Values);
        result.Sort((left, right) => string.CompareOrdinal(left.areaId, right.areaId));
        return result;
    }

    private List<P2VehicleSnapshot> BuildVehicles()
    {
        List<P2VehicleSnapshot> result = new List<P2VehicleSnapshot>();
        List<Car> cars = FindCarsInActiveScene();

        foreach (Car car in cars)
        {
            if (car == null)
            {
                continue;
            }

            NPC_CarController controller = car.GetComponent<NPC_CarController>();
            P2VehicleSnapshotMetadata metadata = car.GetComponent<P2VehicleSnapshotMetadata>();
            ParkingSlot targetSlot = controller != null
                ? controller.targetParkingSlot
                : null;
            ParkingSlot currentSlot = car.currentParkingSlot;
            Vector3 velocity = car.GetCurrentVelocity();

            P2VehicleSnapshot item = new P2VehicleSnapshot
            {
                vehicleId = GetVehicleId(car),
                objectName = car.gameObject.name,
                movementState = controller != null
                    ? controller.moveState.ToString()
                    : (car.isParked ? NPC_CarMoveState.Parked.ToString() : "Unknown"),
                isParked = car.isParked,
                isStopped = car.IsStopped(),
                isStoppedByFrontVehicle = controller != null && controller.isStoppedByFrontCar,
                stopReason = controller != null ? NullToEmpty(controller.currentStopReason) : string.Empty,
                entranceAccessPointId = metadata != null ? NullToEmpty(metadata.entranceAccessPointId) : string.Empty,
                entranceScenePairName = metadata != null ? NullToEmpty(metadata.entranceScenePairName) : string.Empty,
                targetAreaId = targetSlot != null
                    ? GetCanonicalAreaId(targetSlot.areaId)
                    : (metadata != null ? NullToEmpty(metadata.targetAreaId) : string.Empty),
                targetSceneAreaId = targetSlot != null
                    ? NullToEmpty(targetSlot.areaId)
                    : (metadata != null ? NullToEmpty(metadata.targetSceneAreaId) : string.Empty),
                targetSlotId = targetSlot != null
                    ? NullToEmpty(targetSlot.slotId)
                    : (metadata != null ? NullToEmpty(metadata.targetSlotId) : string.Empty),
                currentAreaId = currentSlot != null ? GetCanonicalAreaId(currentSlot.areaId) : string.Empty,
                currentSceneAreaId = currentSlot != null ? NullToEmpty(currentSlot.areaId) : string.Empty,
                currentSlotId = currentSlot != null ? NullToEmpty(currentSlot.slotId) : string.Empty,
                currentTargetWaypointId = controller != null
                    ? GetWaypointId(controller.CurrentTargetWaypointForDebug)
                    : string.Empty,
                spawnSequenceNumber = metadata != null ? metadata.spawnSequenceNumber : 0,
                spawnedAtSimulationTimeSeconds = RoundTime(metadata != null
                    ? metadata.spawnedAtSimulationTimeSeconds
                    : 0f),
                currentSpeedMetersPerSecond = RoundScalar(velocity.magnitude),
                position = ToVector(car.transform.position),
                rotationEuler = ToVector(car.transform.eulerAngles),
                velocity = ToVector(velocity)
            };

            result.Add(item);
        }

        result.Sort((left, right) => string.CompareOrdinal(left.vehicleId, right.vehicleId));
        return result;
    }

    private P2SnapshotSummary BuildSummary(P2SimulationSnapshot snapshot)
    {
        P2SnapshotSummary result = new P2SnapshotSummary
        {
            areaCount = snapshot.areas != null ? snapshot.areas.Count : 0,
            accessPointCount = snapshot.accessPoints != null ? snapshot.accessPoints.Count : 0,
            vehicleCount = snapshot.vehicles != null ? snapshot.vehicles.Count : 0,
            activeScenarioFactorCount = snapshot.activeScenarioFactors != null
                ? snapshot.activeScenarioFactors.Count
                : 0
        };

        if (snapshot.parkingSlots == null)
        {
            return result;
        }

        foreach (P2ParkingSlotSnapshot slot in snapshot.parkingSlots)
        {
            if (slot == null)
            {
                continue;
            }

            result.totalSlotCount++;

            if (slot.isAvailable)
            {
                result.availableSlotCount++;
            }

            if (string.Equals(slot.state, ParkingSlotState.Empty.ToString(), StringComparison.Ordinal))
            {
                result.emptySlotCount++;
            }
            else if (string.Equals(slot.state, ParkingSlotState.Reserved.ToString(), StringComparison.Ordinal))
            {
                result.reservedSlotCount++;
            }
            else if (string.Equals(slot.state, ParkingSlotState.Occupied.ToString(), StringComparison.Ordinal))
            {
                result.occupiedSlotCount++;
            }
            else if (string.Equals(slot.state, ParkingSlotState.Leaving.ToString(), StringComparison.Ordinal))
            {
                result.leavingSlotCount++;
            }
            else if (string.Equals(slot.state, ParkingSlotState.Disabled.ToString(), StringComparison.Ordinal))
            {
                result.disabledSlotCount++;
            }
        }

        return result;
    }

    private List<Car> FindCarsInActiveScene()
    {
        List<Car> result = new List<Car>();
        Scene activeScene = SceneManager.GetActiveScene();

#if UNITY_2023_1_OR_NEWER
        Car[] cars = FindObjectsByType<Car>(FindObjectsSortMode.None);
#else
        Car[] cars = FindObjectsOfType<Car>();
#endif

        foreach (Car car in cars)
        {
            if (car == null || car.gameObject.scene != activeScene)
            {
                continue;
            }

            result.Add(car);
        }

        result.Sort((left, right) =>
            string.CompareOrdinal(GetVehicleId(left), GetVehicleId(right)));
        return result;
    }

    private float GetSimulationTimeSeconds()
    {
        if (scenarioRuntime != null)
        {
            return Mathf.Max(0f, scenarioRuntime.SimulationTimeSeconds);
        }

        if (simulationClock != null)
        {
            return Mathf.Max(0f, simulationClock.SimulationTimeSeconds);
        }

        return Mathf.Max(0f, Time.time);
    }

    private LocalScenarioDefinition GetScenarioDefinition()
    {
        if (scenarioDefinition != null)
        {
            return scenarioDefinition;
        }

        if (scenarioRuntime != null)
        {
            return scenarioRuntime.Definition;
        }

        if (simulationClock != null)
        {
            return simulationClock.scenarioDefinition;
        }

        return null;
    }

    private string GetCanonicalAccessPointId(string scenePairName)
    {
        if (scenarioRuntime != null)
        {
            return NullToEmpty(scenarioRuntime.GetCanonicalAccessPointId(scenePairName));
        }

        if (targetBinding != null)
        {
            return NullToEmpty(targetBinding.GetPrimaryCanonicalAccessPointId(scenePairName));
        }

        return NormalizeFallbackId(scenePairName, "entrance");
    }

    private string GetCanonicalAreaId(string sceneAreaId)
    {
        if (scenarioRuntime != null)
        {
            return NullToEmpty(scenarioRuntime.GetCanonicalAreaId(sceneAreaId));
        }

        if (targetBinding != null)
        {
            return NullToEmpty(targetBinding.GetCanonicalAreaId(sceneAreaId));
        }

        return NormalizeFallbackId(sceneAreaId, "area");
    }

    private static string BuildSnapshotId(
        LocalScenarioDefinition definition,
        string runId,
        long sequenceNumber)
    {
        string scenarioId = definition != null && !string.IsNullOrWhiteSpace(definition.scenarioId)
            ? definition.scenarioId.Trim()
            : "scenario-unconfigured";
        string resolvedRunId = !string.IsNullOrWhiteSpace(runId)
            ? runId.Trim()
            : P2SimulationRunContext.EnsureCurrentRun().runId;

        return scenarioId + "-" + resolvedRunId + "-snapshot-" +
               Math.Max(1L, sequenceNumber).ToString("D8");
    }

    private static string GetVehicleId(NPC_CarController controller)
    {
        if (controller == null)
        {
            return string.Empty;
        }

        return GetVehicleId(controller.GetComponent<Car>());
    }

    private static string GetVehicleId(Car car)
    {
        if (car == null)
        {
            return string.Empty;
        }

        return !string.IsNullOrWhiteSpace(car.carId)
            ? car.carId.Trim()
            : car.gameObject.name;
    }

    private static string GetWaypointId(Waypoint waypoint)
    {
        if (waypoint == null)
        {
            return string.Empty;
        }

        return !string.IsNullOrWhiteSpace(waypoint.waypointId)
            ? waypoint.waypointId.Trim()
            : waypoint.gameObject.name;
    }

    private static ParkingSlotState GetSnapshotSlotState(ParkingSlot slot)
    {
        if (slot == null)
        {
            return ParkingSlotState.Empty;
        }

        // Unity内部では、センサーがまだ車を検知している間はstateがOccupiedでも、
        // isLeaving=trueなら業務上は出庫中です。Snapshotでは排他的な正規化状態として
        // Leavingを優先し、各状態件数の合計が総枠数と一致するようにします。
        if (slot.state != ParkingSlotState.Disabled && slot.isLeaving)
        {
            return ParkingSlotState.Leaving;
        }

        return slot.state;
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

                return GetCanonicalAccessPointId(binding.scenePairName);
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

                return GetCanonicalAreaId(binding.sceneAreaId);
            }
        }

        return originalTargetId;
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
        int digits = Mathf.Clamp(decimalPlaces, 0, 6);
        double rounded = Math.Round((double)value, digits, MidpointRounding.AwayFromZero);

        return Math.Abs(rounded) < Math.Pow(10d, -digits) * 0.5d
            ? 0d
            : rounded;
    }

    private static string NullToEmpty(string value)
    {
        return value ?? string.Empty;
    }

    private static string NormalizeFallbackId(string value, string prefix)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return prefix + "-unknown";
        }

        string normalized = value.Trim().ToLowerInvariant()
            .Replace('_', '-')
            .Replace(' ', '-');

        return normalized.StartsWith(prefix + "-", StringComparison.Ordinal)
            ? normalized
            : prefix + "-" + normalized;
    }
}
