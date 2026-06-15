using System.Collections.Generic;
using UnityEngine;

public class VehicleSpawnManager : MonoBehaviour
{
    [Header("Managers")]
    public ParkingLotManager parkingLotManager;
    public WaypointRouteManager routeManager;

    [Header("Vehicle")]
    public NPC_CarController npcCarPrefab;
    public Transform vehicleParent;

    [Header("Spawn")]
    public Transform spawnPoint;
    public Waypoint entranceWaypoint;
    public Waypoint exitWaypoint;

    [Header("Settings")]
    public bool spawnOnStart = true;
    public bool useRandomSlot = true;

    [Header("Debug")]
    public bool debugLog = true;

    private void Start()
    {
        if (debugLog)
        {
            Debug.Log($"{name}: VehicleSpawnManager Start");
        }

        if (spawnOnStart)
        {
            SpawnVehicleToAvailableSlot();
        }
    }

    [ContextMenu("Spawn Vehicle To Available Slot")]
    public void SpawnVehicleToAvailableSlot()
    {
        if (debugLog)
        {
            Debug.Log($"{name}: SpawnVehicleToAvailableSlot 実行");
        }

        if (!ValidateReferences())
        {
            return;
        }

        ParkingSlot targetSlot = useRandomSlot
            ? parkingLotManager.ReserveRandomAvailableSlot()
            : parkingLotManager.ReserveFirstAvailableSlot();

        if (targetSlot == null)
        {
            Debug.LogWarning($"{name}: 利用可能なParkingSlotがありません。");
            Debug.LogWarning($"{name}: Empty={parkingLotManager.GetSlotCountByState(ParkingSlotState.Empty)}, Reserved={parkingLotManager.GetSlotCountByState(ParkingSlotState.Reserved)}, Occupied={parkingLotManager.GetSlotCountByState(ParkingSlotState.Occupied)}");
            return;
        }

        if (debugLog)
        {
            Debug.Log($"{name}: 選択Slot = {targetSlot.slotId}");
            Debug.Log($"{name}: AccessWaypoint = {targetSlot.accessWaypoint.name}");
        }

        List<Waypoint> routeToSlot = routeManager.FindRoute(
            entranceWaypoint,
            targetSlot.accessWaypoint
        );

        if (routeToSlot == null || routeToSlot.Count == 0)
        {
            Debug.LogWarning($"{name}: 入庫ルートが作れません。{entranceWaypoint.name} → {targetSlot.accessWaypoint.name}");
            parkingLotManager.ReleaseReservation(targetSlot);
            return;
        }

        List<Waypoint> routeToExit = routeManager.FindRoute(
            targetSlot.accessWaypoint,
            exitWaypoint
        );

        if (routeToExit == null || routeToExit.Count == 0)
        {
            Debug.LogWarning($"{name}: 出庫ルートが作れません。{targetSlot.accessWaypoint.name} → {exitWaypoint.name}");
            parkingLotManager.ReleaseReservation(targetSlot);
            return;
        }

        NPC_CarController car = Instantiate(
            npcCarPrefab,
            spawnPoint.position,
            spawnPoint.rotation,
            vehicleParent
        );

        car.targetParkingSlot = targetSlot;
        car.exitRoute = routeToExit;
        car.SetRouteToParkingSlot(routeToSlot, targetSlot);

        if (debugLog)
        {
            Debug.Log($"{name}: 車を生成しました。目的Slot={targetSlot.slotId}, 入庫Route={routeToSlot.Count}, 出庫Route={routeToExit.Count}");
        }
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

        if (spawnPoint == null)
        {
            Debug.LogWarning($"{name}: Spawn Point が設定されていません。");
            return false;
        }

        if (entranceWaypoint == null)
        {
            Debug.LogWarning($"{name}: Entrance Waypoint が設定されていません。");
            return false;
        }

        if (exitWaypoint == null)
        {
            Debug.LogWarning($"{name}: Exit Waypoint が設定されていません。");
            return false;
        }

        return true;
    }
}