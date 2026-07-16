using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 通常移動は次Waypointのみ予約し、ConnectorLaneからMainlineへの合流だけを局所制御します。
/// </summary>
public class MergeTrafficCoordinator : MonoBehaviour
{
    public List<MergePoint> mergePoints = new List<MergePoint>();

    [Tooltip("ONの場合、Start時に子階層とシーン内からMergePointを自動収集します。")]
    public bool autoCollectMergePoints = true;

    [Header("Deadlock Prevention")]
    public List<TrafficDeadlockZone> deadlockZones =
        new List<TrafficDeadlockZone>();

    [Tooltip("Start時にTrafficDeadlockZoneを自動収集します。")]
    public bool autoCollectDeadlockZones = true;

    public bool rebuildLookupOnStart = true;
    public bool logDebug;

    private sealed class PendingMove
    {
        public Waypoint target;

        public readonly List<TrafficDeadlockZone>
            newlyEnteredZones =
                new List<TrafficDeadlockZone>();
    }

    private sealed class Permit
    {
        public MergePoint point;
        public Waypoint target;
        public Waypoint release;
        public bool enteredTarget;
    }

    private readonly Dictionary<Waypoint, List<MergePoint>> byTarget =
        new Dictionary<Waypoint, List<MergePoint>>();

    private readonly Dictionary<
        Waypoint,
        List<TrafficDeadlockZone>>
        zonesByWaypoint =
            new Dictionary<
                Waypoint,
                List<TrafficDeadlockZone>>();

    private readonly Dictionary<NPC_CarController, PendingMove> pending =
        new Dictionary<NPC_CarController, PendingMove>();

    private readonly Dictionary<NPC_CarController, Permit> permits =
        new Dictionary<NPC_CarController, Permit>();

    private readonly Dictionary<NPC_CarController, string> waits =
        new Dictionary<NPC_CarController, string>();

    private readonly Dictionary<NPC_CarController, Waypoint> waitTargets =
        new Dictionary<NPC_CarController, Waypoint>();

    private bool built;

    private void Start()
    {
        if (autoCollectMergePoints)
        {
            CollectMergePoints();
        }

        if (autoCollectDeadlockZones)
        {
            CollectDeadlockZones();
        }

        if (rebuildLookupOnStart)
        {
            RebuildLookup();
        }
    }

    [ContextMenu("Collect Merge Points")]
    public void CollectMergePoints()
    {
        mergePoints.Clear();

#if UNITY_2023_1_OR_NEWER
        MergePoint[] found =
            FindObjectsByType<MergePoint>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );
#else
        MergePoint[] found =
            FindObjectsOfType<MergePoint>(true);
#endif

        foreach (MergePoint point in found)
        {
            if (point != null &&
                !mergePoints.Contains(point))
            {
                mergePoints.Add(point);
            }
        }
    }

    [ContextMenu("Collect Deadlock Zones")]
    public void CollectDeadlockZones()
    {
        deadlockZones.Clear();

#if UNITY_2023_1_OR_NEWER
        TrafficDeadlockZone[] found =
            FindObjectsByType<TrafficDeadlockZone>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );
#else
        TrafficDeadlockZone[] found =
            FindObjectsOfType<TrafficDeadlockZone>(true);
#endif

        foreach (TrafficDeadlockZone zone in found)
        {
            if (zone != null &&
                !deadlockZones.Contains(zone))
            {
                deadlockZones.Add(zone);
            }
        }
    }

    private void Update()
    {
        CleanupInvalidCars();
    }

    [ContextMenu("Rebuild Merge Lookup")]
    public void RebuildLookup()
    {
        byTarget.Clear();
        zonesByWaypoint.Clear();

        foreach (MergePoint point in mergePoints)
        {
            if (point == null || point.mergeTarget == null)
                continue;

            if (!byTarget.TryGetValue(point.mergeTarget, out List<MergePoint> list))
            {
                list = new List<MergePoint>();
                byTarget[point.mergeTarget] = list;
            }

            if (!list.Contains(point))
                list.Add(point);
        }

        foreach (TrafficDeadlockZone zone
                 in deadlockZones)
        {
            if (zone == null ||
                zone.memberWaypoints == null)
            {
                continue;
            }

            foreach (Waypoint waypoint
                     in zone.memberWaypoints)
            {
                if (waypoint == null)
                    continue;

                if (!zonesByWaypoint.TryGetValue(
                        waypoint,
                        out List<TrafficDeadlockZone> list))
                {
                    list =
                        new List<TrafficDeadlockZone>();

                    zonesByWaypoint[waypoint] =
                        list;
                }

                if (!list.Contains(zone))
                    list.Add(zone);
            }
        }

        built = true;
    }

    public bool TryReserveMove(
        NPC_CarController car,
        Waypoint fromWaypoint,
        Waypoint targetWaypoint)
    {
        if (car == null ||
            targetWaypoint == null)
        {
            return false;
        }

        EnsureBuilt();

        if (pending.TryGetValue(
                car,
                out PendingMove oldPending) &&
            oldPending != null)
        {
            if (oldPending.target ==
                targetWaypoint)
            {
                return true;
            }

            ReleasePendingMove(car);
        }

        List<TrafficDeadlockZone> targetZones =
            GetZones(targetWaypoint);

        List<TrafficDeadlockZone> newZones =
            new List<TrafficDeadlockZone>();

        TrafficDeadlockZone blockedZone =
            null;

        foreach (TrafficDeadlockZone zone
                 in targetZones)
        {
            if (zone == null ||
                zone.IsActive(car))
            {
                continue;
            }

            newZones.Add(zone);

            if (!zone.CanEnter(
                    car,
                    targetWaypoint) &&
                blockedZone == null)
            {
                blockedZone = zone;
            }
        }

        if (blockedZone != null)
        {
            CancelUnusedZoneRequests(
                car,
                targetZones
            );

            SetWait(
                car,
                targetWaypoint,
                $"閉路容量待機: Zone={blockedZone.name}, "
                + $"Active={blockedZone.ActiveCount}/"
                + $"{blockedZone.Capacity}, "
                + $"Queue="
                + $"{blockedZone.GetQueuePosition(car)}"
            );

            return false;
        }

        MergePoint point =
            FindPoint(
                fromWaypoint,
                targetWaypoint
            );

        bool mergeAcquired = false;
        bool targetReserved = false;
        Waypoint releaseWaypoint = null;
        bool releaseReserved = false;

        if (point != null)
        {
            if (!point.TryAcquire(car))
            {
                CancelZoneRequests(
                    car,
                    newZones
                );

                SetWait(
                    car,
                    targetWaypoint,
                    $"合流待機: Merge={point.name}, "
                    + $"Queue="
                    + $"{point.GetQueuePosition(car)}"
                );

                return false;
            }

            mergeAcquired = true;

            releaseWaypoint =
                point.reserveReleaseWaypoint
                    ? point.releaseWaypoint
                    : null;
        }

        if (!targetWaypoint.TryReserve(car))
        {
            if (mergeAcquired)
                point.Release(car);

            CancelZoneRequests(
                car,
                newZones
            );

            SetWait(
                car,
                targetWaypoint,
                $"Waypoint予約待機: "
                + $"Target={targetWaypoint.name}"
            );

            return false;
        }

        targetReserved = true;

        if (releaseWaypoint != null &&
            releaseWaypoint != targetWaypoint)
        {
            if (!releaseWaypoint.TryReserve(car))
            {
                if (targetReserved)
                {
                    targetWaypoint
                        .ReleaseReservation(car);
                }

                if (mergeAcquired)
                    point.Release(car);

                CancelZoneRequests(
                    car,
                    newZones
                );

                SetWait(
                    car,
                    targetWaypoint,
                    $"合流待機: "
                    + $"Release={releaseWaypoint.name}"
                );

                return false;
            }

            releaseReserved = true;
        }

        PendingMove move =
            new PendingMove
            {
                target = targetWaypoint
            };

        foreach (TrafficDeadlockZone zone
                 in newZones)
        {
            if (!zone.CommitEntry(
                    car,
                    targetWaypoint))
            {
                foreach (TrafficDeadlockZone committed
                         in move.newlyEnteredZones)
                {
                    committed.Release(car);
                }

                if (releaseReserved &&
                    releaseWaypoint != null)
                {
                    releaseWaypoint
                        .ReleaseReservation(car);
                }

                if (targetReserved)
                {
                    targetWaypoint
                        .ReleaseReservation(car);
                }

                if (mergeAcquired)
                    point.Release(car);

                SetWait(
                    car,
                    targetWaypoint,
                    $"閉路容量確定待機: "
                    + $"Zone={zone.name}"
                );

                return false;
            }

            move.newlyEnteredZones.Add(
                zone
            );
        }

        if (point != null)
        {
            permits[car] =
                new Permit
                {
                    point = point,
                    target = targetWaypoint,
                    release = releaseWaypoint,
                    enteredTarget = false
                };

            foreach (MergePoint other
                     in mergePoints)
            {
                if (other != null &&
                    other != point)
                {
                    other.CancelWait(car);
                }
            }
        }

        pending[car] = move;

        CancelUnusedZoneRequests(
            car,
            targetZones
        );

        ClearWait(car);
        return true;
    }

    public void NotifyArrived(
        NPC_CarController car,
        Waypoint previousWaypoint,
        Waypoint arrivedWaypoint)
    {
        if (car == null || arrivedWaypoint == null)
            return;

        pending.Remove(car);

        UpdateZoneOccupancy(
            car,
            arrivedWaypoint
        );

        if (!permits.TryGetValue(car, out Permit permit) || permit == null)
        {
            ClearWait(car);
            return;
        }

        if (arrivedWaypoint == permit.target)
        {
            permit.enteredTarget = true;
            ClearWait(car);
            return;
        }

        bool reachedRelease =
            permit.release != null &&
            arrivedWaypoint == permit.release;

        bool passedTargetWithoutExplicitRelease =
            permit.release == null &&
            permit.enteredTarget &&
            previousWaypoint == permit.target;

        if (reachedRelease || passedTargetWithoutExplicitRelease)
            ReleasePermit(car, false);

        ClearWait(car);
    }

    public void ReleasePendingMove(NPC_CarController car)
    {
        if (car == null)
            return;

        if (pending.TryGetValue(car, out PendingMove move) &&
            move != null)
        {
            if (move.target != null)
            {
                move.target.ReleaseReservation(car);
            }

            foreach (TrafficDeadlockZone zone
                     in move.newlyEnteredZones)
            {
                if (zone != null)
                    zone.Release(car);
            }
        }

        pending.Remove(car);

        if (permits.TryGetValue(car, out Permit permit) &&
            permit != null &&
            !permit.enteredTarget)
        {
            ReleasePermit(car, true);
        }

        CancelWaits(car);
        ClearWait(car);
    }

    public void LeaveNetwork(NPC_CarController car)
    {
        if (car == null)
            return;

        if (pending.TryGetValue(car, out PendingMove move) &&
            move != null)
        {
            if (move.target != null)
            {
                move.target.ReleaseReservation(car);
            }

            foreach (TrafficDeadlockZone zone
                     in move.newlyEnteredZones)
            {
                if (zone != null)
                    zone.Release(car);
            }
        }

        pending.Remove(car);
        ReleasePermit(car, true);

        foreach (TrafficDeadlockZone zone
                 in deadlockZones)
        {
            if (zone != null)
            {
                zone.Release(car);
                zone.CancelRequest(car);
            }
        }

        CancelWaits(car);
        ClearWait(car);
    }

    public bool ShouldIgnoreForFrontDetection(
        NPC_CarController owner,
        NPC_CarController other,
        Waypoint ownerOccupiedWaypoint,
        Waypoint ownerReservedWaypoint)
    {
        if (owner == null || other == null || owner == other)
            return false;

        foreach (MergePoint point in mergePoints)
        {
            if (point != null &&
                point.IsOwnedBy(owner) &&
                point.GetQueuePosition(other) > 0)
            {
                return true;
            }
        }

        return false;
    }

    public string GetWaitReason(NPC_CarController car)
    {
        if (car == null)
            return "交通予約待機";

        if (!waits.TryGetValue(car, out string reason) ||
            string.IsNullOrEmpty(reason))
        {
            return "通常走行";
        }

        NPC_CarController blocker =
            waitTargets.TryGetValue(car, out Waypoint target) &&
            target != null
                ? target.BlockingCar
                : null;

        return reason +
               $", BlockingCar={(blocker != null ? blocker.name : "none")}";
    }

    private List<TrafficDeadlockZone>
        GetZones(Waypoint waypoint)
    {
        if (waypoint != null &&
            zonesByWaypoint.TryGetValue(
                waypoint,
                out List<TrafficDeadlockZone> zones))
        {
            return zones;
        }

        return EmptyZoneList.Instance;
    }

    private void UpdateZoneOccupancy(
        NPC_CarController car,
        Waypoint arrivedWaypoint)
    {
        List<TrafficDeadlockZone> arrivedZones =
            GetZones(arrivedWaypoint);

        foreach (TrafficDeadlockZone zone
                 in deadlockZones)
        {
            if (zone == null)
                continue;

            bool shouldBeActive =
                arrivedZones.Contains(zone);

            bool isActive =
                zone.IsActive(car);

            if (shouldBeActive &&
                !isActive)
            {
                zone.RegisterExistingCar(car);
            }
            else if (!shouldBeActive &&
                     isActive)
            {
                zone.Release(car);
            }

            zone.CancelRequest(car);
        }
    }

    private void CancelZoneRequests(
        NPC_CarController car,
        List<TrafficDeadlockZone> zones)
    {
        foreach (TrafficDeadlockZone zone
                 in zones)
        {
            if (zone != null)
                zone.CancelRequest(car);
        }
    }

    private void CancelUnusedZoneRequests(
        NPC_CarController car,
        List<TrafficDeadlockZone> targetZones)
    {
        foreach (TrafficDeadlockZone zone
                 in deadlockZones)
        {
            if (zone == null ||
                targetZones.Contains(zone))
            {
                continue;
            }

            zone.CancelRequest(car);
        }
    }

    private static class EmptyZoneList
    {
        public static readonly
            List<TrafficDeadlockZone> Instance =
                new List<TrafficDeadlockZone>();
    }

    private MergePoint FindPoint(Waypoint from, Waypoint target)
    {
        if (from == null || target == null)
            return null;

        if (!byTarget.TryGetValue(target, out List<MergePoint> points))
            return null;

        foreach (MergePoint point in points)
        {
            if (point != null && point.MatchesMove(from, target))
                return point;
        }

        return null;
    }

    private void ReleasePermit(
        NPC_CarController car,
        bool releaseReservations)
    {
        if (car == null ||
            !permits.TryGetValue(car, out Permit permit) ||
            permit == null)
        {
            return;
        }

        if (releaseReservations)
        {
            if (permit.release != null)
                permit.release.ReleaseReservation(car);

            if (!permit.enteredTarget && permit.target != null)
                permit.target.ReleaseReservation(car);
        }

        if (permit.point != null)
            permit.point.Release(car);

        permits.Remove(car);
    }

    private void CancelWaits(NPC_CarController car)
    {
        foreach (MergePoint point in mergePoints)
        {
            if (point != null)
                point.CancelWait(car);
        }
    }

    private void SetWait(
        NPC_CarController car,
        Waypoint target,
        string reason)
    {
        waits[car] = reason;

        if (target != null)
            waitTargets[car] = target;
        else
            waitTargets.Remove(car);
    }

    private void ClearWait(NPC_CarController car)
    {
        if (car == null)
            return;

        waits.Remove(car);
        waitTargets.Remove(car);
    }

    private void EnsureBuilt()
    {
        if (!built)
            RebuildLookup();
    }

    private void CleanupInvalidCars()
    {
        List<NPC_CarController> invalid =
            new List<NPC_CarController>();

        foreach (NPC_CarController car in permits.Keys)
        {
            if (!CanRemainActive(car))
                invalid.Add(car);
        }

        foreach (NPC_CarController car in invalid)
            LeaveNetwork(car);

        invalid.Clear();

        foreach (NPC_CarController car in waits.Keys)
        {
            if (!CanRemainActive(car))
                invalid.Add(car);
        }

        foreach (NPC_CarController car in invalid)
            LeaveNetwork(car);
    }

    private static bool CanRemainActive(NPC_CarController car)
    {
        return car != null &&
               car.isActiveAndEnabled &&
               car.gameObject.activeInHierarchy &&
               car.moveState != NPC_CarMoveState.Finished;
    }
}