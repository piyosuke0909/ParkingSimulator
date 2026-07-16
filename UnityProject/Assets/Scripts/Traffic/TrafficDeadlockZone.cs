using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 有向閉路が車両で完全に埋まることを防ぐ局所容量Zoneです。
///
/// memberWaypointsへ閉路を構成するWaypointを登録し、
/// reservedEmptyWaypointsを1以上にすると、必ず空きセルを残します。
///
/// 例:
/// 4 Waypointの閉路、Reserved Empty = 1
/// Capacity = 3台
///
/// より安全にする場合:
/// Reserved Empty = 2
/// Capacity = 2台
/// </summary>
public class TrafficDeadlockZone : MonoBehaviour
{
    [Header("Zone Definition")]
    public string zoneId;

    [Tooltip("この有向閉路を構成するWaypointです。")]
    public List<Waypoint> memberWaypoints =
        new List<Waypoint>();

    [Tooltip("常に空けておくWaypoint数です。最低1にしてください。")]
    [Min(1)]
    public int reservedEmptyWaypoints = 1;

    [Tooltip("自動計算をOFFにした場合の最大車両数です。")]
    [Min(1)]
    public int manualMaxVehicles = 1;

    public bool calculateCapacityFromMembers = true;

    [Header("Runtime State")]
    [SerializeField]
    private List<NPC_CarController> activeCars =
        new List<NPC_CarController>();

    [SerializeField]
    private List<QueueEntry> queue =
        new List<QueueEntry>();

    [SerializeField]
    private int currentCapacity;

    [SerializeField]
    private int currentActiveCount;

    [Header("Debug")]
    public bool logDebug;

    [Header("Gizmo - Color Only")]
    public bool drawGizmo = true;
    public bool drawOnlyWhenSelected = true;

    public Color availableColor =
        new Color(0.2f, 0.9f, 0.35f, 1f);

    public Color waitingColor =
        new Color(1f, 0.75f, 0.05f, 1f);

    public Color fullColor =
        new Color(1f, 0.2f, 0.1f, 1f);

    [Range(1f, 12f)]
    public float lineWidth = 5f;

    [Min(0f)]
    public float gizmoHeight = 0.9f;

    [Min(0.05f)]
    public float markerRadius = 0.8f;

    [Serializable]
    private sealed class QueueEntry
    {
        public NPC_CarController car;
        public Waypoint entryWaypoint;
        public long order;
    }

    private long nextOrder;

    public int Capacity
    {
        get
        {
            if (!calculateCapacityFromMembers)
            {
                return Mathf.Max(1, manualMaxVehicles);
            }

            int memberCount =
                CountValidMembers();

            return Mathf.Max(
                0,
                memberCount -
                Mathf.Max(1, reservedEmptyWaypoints)
            );
        }
    }

    public int ActiveCount
    {
        get
        {
            Cleanup();
            return activeCars.Count;
        }
    }

    public int WaitingCount
    {
        get
        {
            Cleanup();
            return queue.Count;
        }
    }

    public bool Contains(Waypoint waypoint)
    {
        return waypoint != null &&
               memberWaypoints != null &&
               memberWaypoints.Contains(waypoint);
    }

    public bool IsActive(NPC_CarController car)
    {
        if (car == null)
            return false;

        Cleanup();
        return activeCars.Contains(car);
    }

    /// <summary>
    /// 現在進入可能か確認します。まだActiveには追加しません。
    /// 同じ入口WaypointごとにFIFOを維持します。
    /// </summary>
    public bool CanEnter(
        NPC_CarController car,
        Waypoint entryWaypoint)
    {
        if (car == null ||
            entryWaypoint == null ||
            !Contains(entryWaypoint) ||
            Capacity <= 0)
        {
            return false;
        }

        Cleanup();

        if (activeCars.Contains(car))
        {
            RemoveRequest(car);
            return true;
        }

        QueueEntry request =
            GetOrCreateRequest(car, entryWaypoint);

        if (activeCars.Count >= Capacity)
            return false;

        return GetFirstForEntry(entryWaypoint) == request;
    }

    public bool CommitEntry(
        NPC_CarController car,
        Waypoint entryWaypoint)
    {
        if (!CanEnter(car, entryWaypoint))
            return false;

        if (!activeCars.Contains(car))
            activeCars.Add(car);

        RemoveRequest(car);
        RefreshRuntimeState();

        if (logDebug)
        {
            Debug.Log(
                $"{name}: Entry {car.name} "
                + $"Active={activeCars.Count}/{Capacity}",
                this
            );
        }

        return true;
    }

    public void RegisterExistingCar(
        NPC_CarController car)
    {
        if (car == null)
            return;

        Cleanup();

        if (!activeCars.Contains(car))
            activeCars.Add(car);

        RemoveRequest(car);
        RefreshRuntimeState();
    }

    public void Release(NPC_CarController car)
    {
        if (car == null)
            return;

        activeCars.Remove(car);
        RemoveRequest(car);
        RefreshRuntimeState();
    }

    public void CancelRequest(NPC_CarController car)
    {
        RemoveRequest(car);
        RefreshRuntimeState();
    }

    public int GetQueuePosition(NPC_CarController car)
    {
        if (car == null)
            return 0;

        Cleanup();
        int position = 0;

        foreach (QueueEntry entry in queue)
        {
            if (!IsValid(entry))
                continue;

            position++;

            if (entry.car == car)
                return position;
        }

        return 0;
    }

    private QueueEntry GetOrCreateRequest(
        NPC_CarController car,
        Waypoint entryWaypoint)
    {
        foreach (QueueEntry entry in queue)
        {
            if (entry != null &&
                entry.car == car)
            {
                entry.entryWaypoint =
                    entryWaypoint;

                return entry;
            }
        }

        QueueEntry created =
            new QueueEntry
            {
                car = car,
                entryWaypoint = entryWaypoint,
                order = nextOrder++
            };

        queue.Add(created);
        return created;
    }

    private QueueEntry GetFirstForEntry(
        Waypoint entryWaypoint)
    {
        QueueEntry first = null;

        foreach (QueueEntry entry in queue)
        {
            if (!IsValid(entry) ||
                entry.entryWaypoint != entryWaypoint)
            {
                continue;
            }

            if (first == null ||
                entry.order < first.order)
            {
                first = entry;
            }
        }

        return first;
    }

    private void RemoveRequest(
        NPC_CarController car)
    {
        if (car == null)
            return;

        queue.RemoveAll(
            entry =>
                entry == null ||
                entry.car == car
        );
    }

    private int CountValidMembers()
    {
        if (memberWaypoints == null)
            return 0;

        HashSet<Waypoint> unique =
            new HashSet<Waypoint>();

        foreach (Waypoint waypoint
                 in memberWaypoints)
        {
            if (waypoint != null)
                unique.Add(waypoint);
        }

        return unique.Count;
    }

    private void Cleanup()
    {
        activeCars.RemoveAll(
            car => !CanRemainActive(car)
        );

        queue.RemoveAll(
            entry =>
                !IsValid(entry) ||
                activeCars.Contains(entry.car)
        );

        RefreshRuntimeState();
    }

    private static bool IsValid(
        QueueEntry entry)
    {
        return entry != null &&
               entry.entryWaypoint != null &&
               CanRemainActive(entry.car);
    }

    private static bool CanRemainActive(
        NPC_CarController car)
    {
        return car != null &&
               car.isActiveAndEnabled &&
               car.gameObject.activeInHierarchy &&
               car.moveState !=
                   NPC_CarMoveState.Finished;
    }

    private void Update()
    {
        Cleanup();
    }

    private void OnValidate()
    {
        reservedEmptyWaypoints =
            Mathf.Max(1, reservedEmptyWaypoints);

        manualMaxVehicles =
            Mathf.Max(1, manualMaxVehicles);

        RefreshRuntimeState();
    }

    private void RefreshRuntimeState()
    {
        currentCapacity = Capacity;

        currentActiveCount =
            activeCars != null
                ? activeCars.Count
                : 0;
    }

    private Color GetDisplayColor()
    {
        if (WaitingCount > 0)
            return waitingColor;

        if (Capacity <= 0 ||
            ActiveCount >= Capacity)
        {
            return fullColor;
        }

        return availableColor;
    }

    private void OnDrawGizmos()
    {
#if UNITY_EDITOR
        if (!drawGizmo)
            return;

        if (drawOnlyWhenSelected &&
            Selection.activeGameObject != gameObject)
        {
            return;
        }

        HashSet<Waypoint> members =
            new HashSet<Waypoint>();

        foreach (Waypoint waypoint
                 in memberWaypoints)
        {
            if (waypoint != null)
                members.Add(waypoint);
        }

        Handles.color =
            GetDisplayColor();

        foreach (Waypoint waypoint
                 in members)
        {
            Vector3 position =
                waypoint.transform.position +
                Vector3.up * gizmoHeight;

            Handles.DrawWireDisc(
                position,
                Vector3.up,
                markerRadius
            );

            if (waypoint.nextWaypoints == null)
                continue;

            foreach (Waypoint next
                     in waypoint.nextWaypoints)
            {
                if (next == null ||
                    !members.Contains(next))
                {
                    continue;
                }

                Handles.DrawAAPolyLine(
                    lineWidth,
                    position,
                    next.transform.position +
                    Vector3.up * gizmoHeight
                );
            }
        }
#endif
    }
}
