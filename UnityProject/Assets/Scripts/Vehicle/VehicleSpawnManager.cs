using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

    [Header("Vehicle")]
    public GameObject npcCarPrefab;
    public Transform vehicleParent;

    [Header("Entrance / Exit Pairs")]
    public List<EntranceExitPair> entranceExitPairs = new List<EntranceExitPair>();

    [Tooltip("true: ランダム / false: 順番に使用")]
    public bool useRandomEntranceExitPair = true;

    [Header("Multiple Spawn Settings")]
    public bool spawnOnStart = true;
    public int maxSpawnCount = 10;
    public float spawnInterval = 3f;

    [Header("Spawn Area Check")]
    public bool useSpawnAreaCheck = true;

    [Tooltip("SpawnPointのCube範囲内にあるCar Layerを検知します。")]
    public LayerMask carLayerMask;

    [Tooltip("SpawnPoint CubeのScaleをそのまま判定範囲に使います。")]
    public bool useSpawnPointScaleAsCheckArea = true;

    [Tooltip("useSpawnPointScaleAsCheckArea が false の場合に使う判定サイズです。")]
    public Vector3 spawnCheckBoxSize = new Vector3(12f, 4f, 18f);

    [Tooltip("SpawnPointが埋まっていた場合、次に確認するまでの待機時間です。")]
    public float spawnRetryInterval = 1f;

    public bool drawSpawnAreaGizmo = true;

    [Header("Slot Selection")]
    public bool useRandomSlot = true;

    [Header("Spawn Control")]
    public bool stopWhenNoAvailableSlot = true;
    public bool stopWhenRouteNotFound = false;

    [Header("Debug")]
    public bool debugLog = true;

    private int spawnedCount;
    private bool isSpawning;
    private int nextPairIndex;

    private void Start()
    {
        if (debugLog)
        {
            Debug.Log($"{name}: VehicleSpawnManager Start");
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
            Debug.Log($"{name}: 複数台スポーンを停止しました。");
        }
    }

    private IEnumerator SpawnRoutine()
    {
        isSpawning = true;
        spawnedCount = 0;

        while (spawnedCount < maxSpawnCount)
        {
            bool success = SpawnVehicleToAvailableSlot();

            if (success)
            {
                spawnedCount++;

                if (debugLog)
                {
                    Debug.Log($"{name}: Spawned {spawnedCount}/{maxSpawnCount}");
                }

                yield return new WaitForSeconds(spawnInterval);
            }
            else
            {
                if (debugLog)
                {
                    Debug.Log($"{name}: スポーンできないため待機します。");
                }

                yield return new WaitForSeconds(spawnRetryInterval);
            }
        }

        isSpawning = false;

        if (debugLog)
        {
            Debug.Log($"{name}: 複数台スポーン完了。生成数={spawnedCount}");
        }
    }

    [ContextMenu("Spawn One Vehicle")]
    public void SpawnOneVehicleFromMenu()
    {
        SpawnVehicleToAvailableSlot();
    }

    public bool SpawnVehicleToAvailableSlot()
    {
        if (debugLog)
        {
            Debug.Log($"{name}: SpawnVehicleToAvailableSlot 実行");
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

        ParkingSlot targetSlot = useRandomSlot
            ? parkingLotManager.ReserveRandomAvailableSlot()
            : parkingLotManager.ReserveFirstAvailableSlot();

        if (targetSlot == null)
        {
            Debug.LogWarning($"{name}: 利用可能なParkingSlotがありません。");
            Debug.LogWarning(
                $"{name}: Empty={parkingLotManager.GetSlotCountByState(ParkingSlotState.Empty)}, " +
                $"Reserved={parkingLotManager.GetSlotCountByState(ParkingSlotState.Reserved)}, " +
                $"Occupied={parkingLotManager.GetSlotCountByState(ParkingSlotState.Occupied)}"
            );

            return false;
        }

        if (targetSlot.accessWaypoint == null)
        {
            Debug.LogWarning($"{name}: Target Slot に AccessWaypoint が設定されていません。Slot={targetSlot.slotId}");
            parkingLotManager.ReleaseReservation(targetSlot);
            return false;
        }

        if (debugLog)
        {
            Debug.Log($"{name}: EntranceExitPair = {selectedPair.pairName}");
            Debug.Log($"{name}: Target Slot = {targetSlot.slotId}");
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

        Car carInfo = carObject.GetComponent<Car>();

        if (carInfo != null)
        {
            carInfo.carId = $"NPC_Car_{spawnedCount + 1:000}";
        }

        carController.targetParkingSlot = targetSlot;
        carController.exitRoute = routeToExit;
        carController.SetRouteToParkingSlot(routeToSlot, targetSlot);

        if (debugLog)
        {
            Debug.Log(
                $"{name}: 車を生成しました。Pair={selectedPair.pairName}, " +
                $"目的Slot={targetSlot.slotId}, 入庫Route={routeToSlot.Count}, 出庫Route={routeToExit.Count}"
            );
        }

        return true;
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

    private List<EntranceExitPair> GetValidEntranceExitPairs()
    {
        List<EntranceExitPair> validPairs = new List<EntranceExitPair>();

        if (entranceExitPairs == null)
        {
            return validPairs;
        }

        foreach (EntranceExitPair pair in entranceExitPairs)
        {
            if (pair == null)
            {
                continue;
            }

            if (pair.spawnPoint == null)
            {
                continue;
            }

            if (pair.entranceWaypoint == null)
            {
                continue;
            }

            if (pair.exitWaypoint == null)
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

        Vector3 halfExtents = boxSize * 0.5f;

        Collider[] hits = Physics.OverlapBox(
            spawnPoint.position,
            halfExtents,
            spawnPoint.rotation,
            carLayerMask
        );

        foreach (Collider hit in hits)
        {
            Car hitCar = hit.GetComponentInParent<Car>();

            if (hitCar == null)
            {
                continue;
            }

            return false;
        }

        return true;
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
        if (!drawSpawnAreaGizmo)
        {
            return;
        }

        if (entranceExitPairs == null)
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
