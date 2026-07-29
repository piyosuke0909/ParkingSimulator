using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[System.Serializable]
public class EntranceExitPair
{
    public string pairName;
    public Transform spawnPoint;
    public Waypoint entranceWaypoint;
    public Waypoint exitWaypoint;
}

public class VehicleSpawnManager : MonoBehaviour
{
    [Header("Managers")]
    public ParkingLotManager parkingLotManager;
    public WaypointRouteManager routeManager;

    [Header("P1 Scenario (Optional)")]
    public bool useScenarioSystem = true;
    public ScenarioFactorRuntime scenarioRuntime;
    public ScenarioRandomService scenarioRandomService;
    public ScenarioRunLogger scenarioRunLogger;

    [Header("Vehicle")]
    public GameObject npcCarPrefab;
    public Transform vehicleParent;

    [Header("Entrance / Exit Pairs")]
    public List<EntranceExitPair> entranceExitPairs = new List<EntranceExitPair>();

    [Tooltip("true: 重み付きランダム / false: 順番に使用")]
    public bool useRandomEntranceExitPair = true;

    [Header("Concurrent Spawn Settings")]
    public bool spawnOnStart = true;

    [FormerlySerializedAs("maxSpawnCount")]
    [Min(1)]
    [Tooltip("累計生成数ではなく、Scene内に同時に存在できる車両数です。")]
    public int maxConcurrentVehicles = 60;

    [Tooltip("ArrivalRateSegmentが有効でない時間帯の既定生成間隔です。既定流入率は 60 / spawnInterval 台/分です。")]
    [Min(0.01f)]
    public float spawnInterval = 5f;

    [Tooltip("Play開始時に既存のNPC車両も同時存在台数へ登録します。")]
    public bool includeExistingVehiclesOnStart = true;

    [Header("Spawn Area Check")]
    public bool useSpawnAreaCheck = true;
    public LayerMask carLayerMask;
    public bool useSpawnPointScaleAsCheckArea = true;
    public Vector3 spawnCheckBoxSize = new Vector3(12f, 4f, 18f);

    [Tooltip("SpawnPointが埋まっている場合、空きが確認されるまでの待機時間です。")]
    public float spawnRetryInterval = 1f;
    public bool drawSpawnAreaGizmo = true;

    [Header("Slot Selection")]
    public bool useRandomSlot = true;

    [Tooltip("Scenario使用時は、エリアを重み付き抽選してからエリア内の空き枠を固定seedで選びます。")]
    public bool useScenarioAreaPreference = true;

    [Header("Spawn Control")]
    public bool stopWhenNoAvailableSlot = false;
    public bool stopWhenRouteNotFound = false;

    [Header("Debug")]
    public bool debugLog = true;

    [Header("Runtime (Read Only)")]
    [SerializeField]
    private int activeVehicleCount;

    [SerializeField]
    private int totalSpawnedCount;

    private readonly HashSet<GameObject> activeVehicles = new HashSet<GameObject>();
    private bool isSpawning;
    private int nextPairIndex;
    private int spawnSequenceNumber;

    public int ActiveVehicleCount => activeVehicleCount;

    public int TotalSpawnedCount => totalSpawnedCount;

    // 既存コードからの参照互換用。意味は累計上限ではなく同時存在台数上限です。
    public int maxSpawnCount
    {
        get => maxConcurrentVehicles;
        set => maxConcurrentVehicles = value;
    }

    private void Awake()
    {
        ResolveScenarioReferences();
    }

    private void Start()
    {
        if (includeExistingVehiclesOnStart)
        {
            RegisterExistingVehicles();
        }

        if (debugLog)
        {
            Debug.Log(
                $"{name}: VehicleSpawnManager Start. " +
                $"Active={ActiveVehicleCount}/{maxConcurrentVehicles}"
            );
        }

        if (spawnOnStart)
        {
            StartMultipleSpawn();
        }
    }

    [ContextMenu("Start Multiple Spawn")]
    public void StartMultipleSpawn()
    {
        if (isSpawning)
        {
            return;
        }

        StartCoroutine(SpawnRoutine());
    }

    [ContextMenu("Stop Multiple Spawn")]
    public void StopMultipleSpawn()
    {
        StopAllCoroutines();
        isSpawning = false;

        if (debugLog)
        {
            Debug.Log($"{name}: 車両生成を停止しました。");
        }
    }

    [ContextMenu("Rebuild Active Vehicle Set")]
    public void RegisterExistingVehicles()
    {
        activeVehicles.Clear();

        NPC_CarController[] controllers;

        if (vehicleParent != null)
        {
            controllers = vehicleParent.GetComponentsInChildren<NPC_CarController>(true);
        }
        else
        {
#if UNITY_2023_1_OR_NEWER
            controllers = FindObjectsByType<NPC_CarController>(FindObjectsSortMode.None);
#else
            controllers = FindObjectsOfType<NPC_CarController>();
#endif
        }

        foreach (NPC_CarController controller in controllers)
        {
            if (controller == null)
            {
                continue;
            }

            RegisterActiveVehicle(controller.gameObject);
        }

        RefreshActiveVehicleCount();
    }

    private IEnumerator SpawnRoutine()
    {
        isSpawning = true;
        bool firstNonScenarioAttempt = true;

        while (true)
        {
            ResolveScenarioReferences();
            CleanupDestroyedVehicles();

            if (useScenarioSystem && scenarioRuntime != null && scenarioRuntime.IsScenarioCompleted)
            {
                break;
            }

            if (ActiveVehicleCount >= Mathf.Max(1, maxConcurrentVehicles))
            {
                yield return WaitForSimulationSeconds(spawnRetryInterval);
                continue;
            }

            if (useScenarioSystem && scenarioRuntime != null)
            {
                string scheduledSourceId = scenarioRuntime.GetArrivalRateSourceId();
                ArrivalDistribution scheduledDistribution = scenarioRuntime.GetArrivalDistribution();
                float baseVehiclesPerMinute = scenarioRuntime.GetBaseVehiclesPerMinute(spawnInterval);
                float nextDelay = scenarioRuntime.GetNextSpawnDelay(spawnInterval);

                if (float.IsInfinity(nextDelay))
                {
                    yield return WaitForSimulationSeconds(spawnRetryInterval);
                    continue;
                }

                scenarioRunLogger?.LogArrivalScheduled(
                    scenarioRuntime.SimulationTimeSeconds,
                    scheduledSourceId,
                    scheduledDistribution,
                    baseVehiclesPerMinute,
                    scenarioRuntime.GetArrivalRateMultiplier(),
                    scenarioRuntime.GetEffectiveVehiclesPerMinute(spawnInterval),
                    nextDelay
                );

                yield return WaitForSimulationSeconds(nextDelay);

                if (scenarioRuntime.IsScenarioCompleted)
                {
                    break;
                }

                // 待機中に既定流入と時間帯Segmentが切り替わった場合は、
                // 新しい流入条件で次回時刻を引き直します。
                if (scenarioRuntime.GetArrivalRateSourceId() != scheduledSourceId)
                {
                    continue;
                }
            }
            else if (!firstNonScenarioAttempt)
            {
                yield return WaitForSimulationSeconds(spawnInterval);
            }

            firstNonScenarioAttempt = false;

            while (ActiveVehicleCount >= Mathf.Max(1, maxConcurrentVehicles))
            {
                if (useScenarioSystem && scenarioRuntime != null && scenarioRuntime.IsScenarioCompleted)
                {
                    break;
                }

                yield return WaitForSimulationSeconds(spawnRetryInterval);
            }

            if (useScenarioSystem && scenarioRuntime != null && scenarioRuntime.IsScenarioCompleted)
            {
                break;
            }

            bool success = false;

            while (!success)
            {
                if (useScenarioSystem && scenarioRuntime != null && scenarioRuntime.IsScenarioCompleted)
                {
                    break;
                }

                if (ActiveVehicleCount >= Mathf.Max(1, maxConcurrentVehicles))
                {
                    yield return WaitForSimulationSeconds(spawnRetryInterval);
                    continue;
                }

                success = SpawnVehicleToAvailableSlot();

                if (!success)
                {
                    if (debugLog)
                    {
                        Debug.Log($"{name}: 到着車両を生成できないため、同じ到着要求を再試行します。");
                    }

                    yield return WaitForSimulationSeconds(spawnRetryInterval);
                }
            }
        }

        isSpawning = false;

        if (debugLog)
        {
            Debug.Log(
                $"{name}: シナリオ終了または手動停止により生成処理を終了しました。" +
                $"累計生成数={totalSpawnedCount}, 現在存在数={ActiveVehicleCount}"
            );
        }
    }

    [ContextMenu("Spawn One Vehicle")]
    public void SpawnOneVehicleFromMenu()
    {
        SpawnVehicleToAvailableSlot();
    }

    public bool SpawnVehicleToAvailableSlot()
    {
        CleanupDestroyedVehicles();

        if (ActiveVehicleCount >= Mathf.Max(1, maxConcurrentVehicles))
        {
            if (debugLog)
            {
                Debug.Log($"{name}: 同時存在台数上限です。{ActiveVehicleCount}/{maxConcurrentVehicles}");
            }

            return false;
        }

        if (!ValidateReferences())
        {
            return false;
        }

        EntranceExitPair selectedPair = GetAvailableEntranceExitPair();

        if (selectedPair == null)
        {
            if (debugLog)
            {
                Debug.Log($"{name}: 使用可能なSpawnPointがありません。全入口が混雑中の可能性があります。");
            }

            return false;
        }

        string selectedSceneAreaId;
        ParkingSlot targetSlot = ReserveTargetSlot(out selectedSceneAreaId);

        if (targetSlot == null)
        {
            Debug.LogWarning($"{name}: 利用可能なParkingSlotがありません。");
            Debug.LogWarning(
                $"{name}: Empty={parkingLotManager.GetSlotCountByState(ParkingSlotState.Empty)}, " +
                $"Reserved={parkingLotManager.GetSlotCountByState(ParkingSlotState.Reserved)}, " +
                $"Occupied={parkingLotManager.GetSlotCountByState(ParkingSlotState.Occupied)}"
            );

            if (stopWhenNoAvailableSlot)
            {
                StopMultipleSpawn();
            }

            return false;
        }

        if (targetSlot.accessWaypoint == null)
        {
            Debug.LogWarning($"{name}: Target Slot に AccessWaypoint が設定されていません。Slot={targetSlot.slotId}");
            parkingLotManager.ReleaseReservation(targetSlot);
            return false;
        }

        List<Waypoint> routeToSlot = routeManager.FindRoute(
            selectedPair.entranceWaypoint,
            targetSlot.accessWaypoint
        );

        if (routeToSlot == null || routeToSlot.Count == 0)
        {
            Debug.LogWarning(
                $"{name}: 入庫ルートが作れません。{selectedPair.entranceWaypoint.name} → {targetSlot.accessWaypoint.name}"
            );

            parkingLotManager.ReleaseReservation(targetSlot);

            if (stopWhenRouteNotFound)
            {
                StopMultipleSpawn();
            }

            return false;
        }

        List<Waypoint> routeToExit = routeManager.FindRoute(
            targetSlot.accessWaypoint,
            selectedPair.exitWaypoint
        );

        if (routeToExit == null || routeToExit.Count == 0)
        {
            Debug.LogWarning(
                $"{name}: 出庫ルートが作れません。{targetSlot.accessWaypoint.name} → {selectedPair.exitWaypoint.name}"
            );

            parkingLotManager.ReleaseReservation(targetSlot);

            if (stopWhenRouteNotFound)
            {
                StopMultipleSpawn();
            }

            return false;
        }

        GameObject carObject = Instantiate(
            npcCarPrefab,
            selectedPair.spawnPoint.position,
            selectedPair.spawnPoint.rotation,
            vehicleParent
        );

        NPC_CarController carController = carObject.GetComponent<NPC_CarController>();

        if (carController == null)
        {
            Debug.LogWarning($"{name}: 生成した車に NPC_CarController が付いていません。");
            Destroy(carObject);
            parkingLotManager.ReleaseReservation(targetSlot);
            return false;
        }

        if (!targetSlot.TryAssignReservedOwner(carController))
        {
            Debug.LogWarning($"{name}: 予約Slotの所有者設定に失敗しました。Slot={targetSlot.slotId}");
            Destroy(carObject);
            parkingLotManager.ReleaseReservation(targetSlot);
            return false;
        }

        spawnSequenceNumber++;
        totalSpawnedCount++;

        Car carInfo = carObject.GetComponent<Car>();

        if (carInfo != null)
        {
            carInfo.carId = $"NPC_Car_{spawnSequenceNumber:000}";
        }

        // P2 Snapshot用の読み取り専用メタデータです。
        // 車両の走行・駐車制御には使用しません。
        P2VehicleSnapshotMetadata snapshotMetadata =
            carObject.GetComponent<P2VehicleSnapshotMetadata>();

        if (snapshotMetadata == null)
        {
            snapshotMetadata = carObject.AddComponent<P2VehicleSnapshotMetadata>();
        }

        string snapshotAccessPointId = scenarioRuntime != null
            ? scenarioRuntime.GetCanonicalAccessPointId(selectedPair.pairName)
            : selectedPair.pairName;
        string snapshotAreaId = scenarioRuntime != null
            ? scenarioRuntime.GetCanonicalAreaId(targetSlot.areaId)
            : targetSlot.areaId;
        float snapshotSimulationTime = scenarioRuntime != null
            ? scenarioRuntime.SimulationTimeSeconds
            : Time.time;

        snapshotMetadata.Initialize(
            spawnSequenceNumber,
            snapshotAccessPointId,
            selectedPair.pairName,
            snapshotAreaId,
            targetSlot.areaId,
            targetSlot.slotId,
            snapshotSimulationTime
        );

        carController.targetParkingSlot = targetSlot;
        carController.exitRoute = routeToExit;
        carController.SetRouteToParkingSlot(routeToSlot, targetSlot);

        RegisterActiveVehicle(carObject);
        LogScenarioSpawn(selectedPair, targetSlot);

        if (debugLog)
        {
            Debug.Log(
                $"{name}: 車を生成しました。Pair={selectedPair.pairName}, " +
                $"Area={selectedSceneAreaId}, Slot={targetSlot.slotId}, " +
                $"Active={ActiveVehicleCount}/{maxConcurrentVehicles}, Total={totalSpawnedCount}"
            );
        }

        return true;
    }

    public void NotifyVehicleDestroyed(GameObject vehicleObject)
    {
        string vehicleName = vehicleObject != null ? vehicleObject.name : "destroyed-vehicle";

        if (vehicleObject != null)
        {
            activeVehicles.Remove(vehicleObject);
        }

        CleanupDestroyedVehicles();

        float time = scenarioRuntime != null
            ? scenarioRuntime.SimulationTimeSeconds
            : Time.time;

        scenarioRunLogger?.LogVehicleRemoved(
            time,
            vehicleName,
            ActiveVehicleCount,
            maxConcurrentVehicles
        );
    }

    private void RegisterActiveVehicle(GameObject carObject)
    {
        if (carObject == null)
        {
            return;
        }

        activeVehicles.Add(carObject);

        SpawnedVehicleTracker tracker = carObject.GetComponent<SpawnedVehicleTracker>();

        if (tracker == null)
        {
            tracker = carObject.AddComponent<SpawnedVehicleTracker>();
        }

        tracker.Initialize(this);
        RefreshActiveVehicleCount();
    }

    private void CleanupDestroyedVehicles()
    {
        activeVehicles.RemoveWhere(item => item == null);
        RefreshActiveVehicleCount();
    }

    private void RefreshActiveVehicleCount()
    {
        activeVehicleCount = activeVehicles.Count;
    }

    private ParkingSlot ReserveTargetSlot(out string selectedSceneAreaId)
    {
        selectedSceneAreaId = string.Empty;

        if (useScenarioSystem &&
            useScenarioAreaPreference &&
            useRandomSlot &&
            scenarioRandomService != null)
        {
            List<string> availableAreaIds = parkingLotManager.GetAvailableAreaIdsWithAccessWaypoint();

            if (availableAreaIds.Count == 0)
            {
                return null;
            }

            List<float> weights = new List<float>(availableAreaIds.Count);
            List<int> availableSlotCounts = new List<int>(availableAreaIds.Count);
            List<string> canonicalAreaIds = new List<string>(availableAreaIds.Count);

            foreach (string sceneAreaId in availableAreaIds)
            {
                float weight = scenarioRuntime != null
                    ? scenarioRuntime.GetAreaPreferenceWeight(sceneAreaId)
                    : 1f;

                weights.Add(weight);
                availableSlotCounts.Add(parkingLotManager.GetAvailableSlotCountInArea(sceneAreaId));
                canonicalAreaIds.Add(
                    scenarioRuntime != null
                        ? scenarioRuntime.GetCanonicalAreaId(sceneAreaId)
                        : sceneAreaId
                );
            }

            int areaIndex = scenarioRandomService.ChooseWeightedIndex(weights);

            if (areaIndex < 0 || areaIndex >= availableAreaIds.Count)
            {
                return null;
            }

            selectedSceneAreaId = availableAreaIds[areaIndex];

            scenarioRunLogger?.LogParkingAreaSelected(
                scenarioRuntime != null ? scenarioRuntime.SimulationTimeSeconds : Time.time,
                canonicalAreaIds,
                availableSlotCounts,
                weights,
                canonicalAreaIds[areaIndex],
                selectedSceneAreaId
            );

            return parkingLotManager.ReserveRandomAvailableSlotInArea(
                selectedSceneAreaId,
                scenarioRandomService
            );
        }

        ParkingSlot slot;

        if (useRandomSlot)
        {
            slot = useScenarioSystem && scenarioRandomService != null
                ? parkingLotManager.ReserveRandomAvailableSlot(scenarioRandomService)
                : parkingLotManager.ReserveRandomAvailableSlot();
        }
        else
        {
            slot = parkingLotManager.ReserveFirstAvailableSlot();
        }

        if (slot != null)
        {
            selectedSceneAreaId = slot.areaId;
        }

        return slot;
    }

    private EntranceExitPair GetAvailableEntranceExitPair()
    {
        List<EntranceExitPair> validPairs = GetValidEntranceExitPairs();

        if (validPairs.Count == 0)
        {
            return null;
        }

        List<EntranceExitPair> availablePairs = new List<EntranceExitPair>();

        foreach (EntranceExitPair pair in validPairs)
        {
            if (CanSpawnAt(pair.spawnPoint))
            {
                availablePairs.Add(pair);
            }
        }

        if (availablePairs.Count == 0)
        {
            return null;
        }

        if (useRandomEntranceExitPair)
        {
            if (useScenarioSystem && scenarioRandomService != null)
            {
                List<float> weights = new List<float>(availablePairs.Count);

                foreach (EntranceExitPair pair in availablePairs)
                {
                    float weight = scenarioRuntime != null
                        ? scenarioRuntime.GetAccessPointPreferenceWeight(pair.pairName)
                        : 1f;

                    weights.Add(weight);
                }

                int weightedIndex = scenarioRandomService.ChooseWeightedIndex(weights);

                if (weightedIndex >= 0 && weightedIndex < availablePairs.Count)
                {
                    EntranceExitPair selectedPair = availablePairs[weightedIndex];
                    LogEntranceSelection(availablePairs, weights, selectedPair);
                    return selectedPair;
                }
            }

            int index = Random.Range(0, availablePairs.Count);
            return availablePairs[index];
        }

        for (int i = 0; i < availablePairs.Count; i++)
        {
            EntranceExitPair pair = availablePairs[nextPairIndex % availablePairs.Count];
            nextPairIndex++;

            if (pair != null)
            {
                return pair;
            }
        }

        return null;
    }

    private void LogEntranceSelection(
        List<EntranceExitPair> availablePairs,
        List<float> weights,
        EntranceExitPair selectedPair)
    {
        ResolveScenarioReferences();

        if (!useScenarioSystem ||
            scenarioRuntime == null ||
            scenarioRunLogger == null ||
            availablePairs == null ||
            weights == null ||
            selectedPair == null)
        {
            return;
        }

        List<string> pairNames = new List<string>(availablePairs.Count);

        foreach (EntranceExitPair pair in availablePairs)
        {
            pairNames.Add(pair != null ? pair.pairName : "null");
        }

        scenarioRunLogger.LogEntranceSelected(
            scenarioRuntime.SimulationTimeSeconds,
            pairNames,
            weights,
            selectedPair.pairName,
            scenarioRuntime.GetCanonicalAccessPointId(selectedPair.pairName)
        );
    }

    private List<EntranceExitPair> GetValidEntranceExitPairs()
    {
        List<EntranceExitPair> validPairs = new List<EntranceExitPair>();

        if (entranceExitPairs == null)
        {
            return validPairs;
        }

        foreach (EntranceExitPair pair in entranceExitPairs)
        {
            if (pair == null ||
                pair.spawnPoint == null ||
                pair.entranceWaypoint == null ||
                pair.exitWaypoint == null)
            {
                continue;
            }

            validPairs.Add(pair);
        }

        return validPairs;
    }

    private bool CanSpawnAt(Transform spawnPoint)
    {
        if (!useSpawnAreaCheck)
        {
            return true;
        }

        if (spawnPoint == null)
        {
            return false;
        }

        Vector3 boxSize = useSpawnPointScaleAsCheckArea
            ? spawnPoint.lossyScale
            : spawnCheckBoxSize;

        Collider[] hits = Physics.OverlapBox(
            spawnPoint.position,
            boxSize * 0.5f,
            spawnPoint.rotation,
            carLayerMask
        );

        foreach (Collider hit in hits)
        {
            if (hit.GetComponentInParent<Car>() != null)
            {
                return false;
            }
        }

        return true;
    }

    private void ResolveScenarioReferences()
    {
        if (!useScenarioSystem)
        {
            return;
        }

        if (scenarioRuntime == null)
        {
            scenarioRuntime = ScenarioFactorRuntime.Instance;
        }

        if (scenarioRandomService == null &&
            scenarioRuntime != null &&
            scenarioRuntime.RandomService != null)
        {
            scenarioRandomService = scenarioRuntime.RandomService;
        }

        if (scenarioRandomService == null)
        {
            scenarioRandomService = ScenarioRandomService.Instance;
        }

        if (scenarioRunLogger == null && scenarioRuntime != null)
        {
            scenarioRunLogger = scenarioRuntime.RunLogger;
        }
    }

    private IEnumerator WaitForSimulationSeconds(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        ResolveScenarioReferences();

        SimulationClock clock = useScenarioSystem && scenarioRuntime != null
            ? scenarioRuntime.Clock
            : null;

        if (clock == null)
        {
            yield return new WaitForSeconds(seconds);
            yield break;
        }

        if (seconds <= 0f)
        {
            yield break;
        }

        double elapsedSimulationSeconds = 0d;
        double previousAdvancedSeconds = clock.TotalAdvancedSimulationSeconds;

        while (elapsedSimulationSeconds < seconds)
        {
            if (!clock.IsRunning && scenarioRuntime != null && scenarioRuntime.IsScenarioCompleted)
            {
                yield break;
            }

            yield return null;

            if (clock == null)
            {
                yield break;
            }

            double currentAdvancedSeconds = clock.TotalAdvancedSimulationSeconds;
            double frameIncrement = currentAdvancedSeconds - previousAdvancedSeconds;

            // Clockが自然に前進させた秒数だけを積算します。
            // SetSimulationTimeによる巻き戻し・早送りや、表示時刻のループは
            // 待機秒数そのものを増減させません。
            if (frameIncrement > 0d &&
                !double.IsNaN(frameIncrement) &&
                !double.IsInfinity(frameIncrement))
            {
                elapsedSimulationSeconds += frameIncrement;
            }

            previousAdvancedSeconds = currentAdvancedSeconds;
        }
    }

    private void LogScenarioSpawn(EntranceExitPair selectedPair, ParkingSlot targetSlot)
    {
        ResolveScenarioReferences();

        if (!useScenarioSystem ||
            scenarioRuntime == null ||
            scenarioRunLogger == null ||
            selectedPair == null ||
            targetSlot == null)
        {
            return;
        }

        int seed = scenarioRandomService != null
            ? scenarioRandomService.CurrentSeed
            : 0;

        scenarioRunLogger.LogVehicleSpawned(
            spawnSequenceNumber,
            scenarioRuntime.SimulationTimeSeconds,
            seed,
            scenarioRuntime.GetActiveFactorIdsCsv(),
            scenarioRuntime.GetArrivalRateMultiplier(),
            scenarioRuntime.GetVehicleSpeedMultiplier(),
            scenarioRuntime.GetEffectiveVehiclesPerMinute(spawnInterval),
            scenarioRuntime.GetCanonicalAccessPointId(selectedPair.pairName),
            selectedPair.pairName,
            targetSlot.slotId,
            scenarioRuntime.GetCanonicalAreaId(targetSlot.areaId),
            ActiveVehicleCount,
            maxConcurrentVehicles
        );
    }

    private bool ValidateReferences()
    {
        if (parkingLotManager == null)
        {
            Debug.LogWarning($"{name}: ParkingLotManager が設定されていません。");
            return false;
        }

        if (routeManager == null)
        {
            Debug.LogWarning($"{name}: WaypointRouteManager が設定されていません。");
            return false;
        }

        if (npcCarPrefab == null)
        {
            Debug.LogWarning($"{name}: NPC Car Prefab が設定されていません。");
            return false;
        }

        if (entranceExitPairs == null || entranceExitPairs.Count == 0)
        {
            Debug.LogWarning($"{name}: Entrance Exit Pairs が設定されていません。");
            return false;
        }

        if (GetValidEntranceExitPairs().Count == 0)
        {
            Debug.LogWarning($"{name}: 有効な Entrance Exit Pair がありません。");
            return false;
        }

        return true;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawSpawnAreaGizmo || entranceExitPairs == null)
        {
            return;
        }

        Gizmos.color = Color.red;

        foreach (EntranceExitPair pair in entranceExitPairs)
        {
            if (pair == null || pair.spawnPoint == null)
            {
                continue;
            }

            Vector3 boxSize = useSpawnPointScaleAsCheckArea
                ? pair.spawnPoint.lossyScale
                : spawnCheckBoxSize;

            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(
                pair.spawnPoint.position,
                pair.spawnPoint.rotation,
                boxSize
            );
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            Gizmos.matrix = oldMatrix;
        }
    }
}
