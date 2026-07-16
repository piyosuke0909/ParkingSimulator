using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 縦方向のConnectorLaneから横方向のMainlineへ入る1か所の局所合流です。
/// </summary>
public class MergePoint : MonoBehaviour
{
    [Header("Manual Merge Definition")]
    public string mergeId;
    public List<Waypoint> sideApproachWaypoints = new List<Waypoint>();
    public Waypoint mergeTarget;
    public Waypoint releaseWaypoint;
    public Waypoint mainlinePreviousWaypoint;

    [Header("Admission")]
    public bool requireMainlinePreviousClear = true;
    public bool reserveReleaseWaypoint = true;

    [Header("Runtime")]
    public NPC_CarController owner;

    [SerializeField]
    private List<QueueEntry> queue = new List<QueueEntry>();

    [Header("Debug")]
    public bool logDebug;

    [Header("Gizmo - Color Only")]
    public bool drawGizmo = true;
    public bool drawOnlyWhenSelected = false;
    public Color freeColor = new Color(0.2f, 0.9f, 0.35f, 1f);
    public Color waitingColor = new Color(1f, 0.75f, 0.05f, 1f);
    public Color occupiedColor = new Color(1f, 0.2f, 0.1f, 1f);
    [Range(1f, 12f)] public float lineWidth = 6f;
    [Min(0f)] public float gizmoHeight = 0.7f;
    [Min(0.05f)] public float markerRadius = 0.75f;

    [Serializable]
    private sealed class QueueEntry
    {
        public NPC_CarController car;
        public long order;
    }

    private long nextOrder;

    public int WaitingCount
    {
        get
        {
            CleanupQueue();
            return queue.Count;
        }
    }

    public bool MatchesMove(Waypoint fromWaypoint, Waypoint targetWaypoint)
    {
        return targetWaypoint == mergeTarget &&
               fromWaypoint != null &&
               sideApproachWaypoints.Contains(fromWaypoint);
    }

    public bool IsOwnedBy(NPC_CarController car)
    {
        return car != null && owner == car;
    }

    public bool TryAcquire(NPC_CarController car)
    {
        if (car == null || mergeTarget == null)
            return false;

        CleanupQueue();

        if (owner == car)
        {
            RemoveFromQueue(car);
            return true;
        }

        QueueEntry entry = GetOrCreateEntry(car);

        if (owner != null || GetFirstValidEntry() != entry)
            return false;

        if (requireMainlinePreviousClear &&
            mainlinePreviousWaypoint != null &&
            !mainlinePreviousWaypoint.IsAvailableFor(car))
            return false;

        if (!mergeTarget.IsAvailableFor(car))
            return false;

        if (reserveReleaseWaypoint &&
            releaseWaypoint != null &&
            !releaseWaypoint.IsAvailableFor(car))
            return false;

        owner = car;
        RemoveFromQueue(car);

        if (logDebug)
            Debug.Log($"{name}: acquired by {car.name}", this);

        return true;
    }

    public void Release(NPC_CarController car)
    {
        if (car == null)
            return;

        RemoveFromQueue(car);

        if (owner == car)
        {
            owner = null;

            if (logDebug)
                Debug.Log($"{name}: released by {car.name}", this);
        }
    }

    public void CancelWait(NPC_CarController car)
    {
        RemoveFromQueue(car);
    }

    public int GetQueuePosition(NPC_CarController car)
    {
        if (car == null)
            return 0;

        CleanupQueue();
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

    private QueueEntry GetOrCreateEntry(NPC_CarController car)
    {
        foreach (QueueEntry entry in queue)
        {
            if (entry != null && entry.car == car)
                return entry;
        }

        QueueEntry created = new QueueEntry
        {
            car = car,
            order = nextOrder++
        };

        queue.Add(created);
        return created;
    }

    private QueueEntry GetFirstValidEntry()
    {
        QueueEntry first = null;

        foreach (QueueEntry entry in queue)
        {
            if (!IsValid(entry))
                continue;

            if (first == null || entry.order < first.order)
                first = entry;
        }

        return first;
    }

    private void RemoveFromQueue(NPC_CarController car)
    {
        if (car == null)
            return;

        queue.RemoveAll(entry => entry == null || entry.car == car);
    }

    private void CleanupQueue()
    {
        queue.RemoveAll(entry => !IsValid(entry));

        if (!CanRemainActive(owner))
            owner = null;
    }

    private static bool IsValid(QueueEntry entry)
    {
        return entry != null && CanRemainActive(entry.car);
    }

    private static bool CanRemainActive(NPC_CarController car)
    {
        return car != null &&
               car.isActiveAndEnabled &&
               car.gameObject.activeInHierarchy &&
               car.moveState != NPC_CarMoveState.Finished;
    }

    private void Update()
    {
        CleanupQueue();
    }

    private void OnDrawGizmos()
    {
#if UNITY_EDITOR
        TrafficGizmoSettings global =
            TrafficGizmoSettings.Instance;

        bool shouldDraw =
            global != null
                ? global.ShouldDrawMergePoint()
                : drawGizmo;

        bool selectedOnly =
            global != null
                ? global.ShouldDrawMergeOnlyWhenSelected()
                : drawOnlyWhenSelected;

        if (!shouldDraw)
            return;

        if (selectedOnly &&
            UnityEditor.Selection.activeGameObject != gameObject)
            return;

        Color color = owner != null
            ? occupiedColor
            : WaitingCount > 0 ? waitingColor : freeColor;

        Handles.color = color;

        if (mergeTarget == null)
            return;

        Vector3 target = mergeTarget.transform.position + Vector3.up * gizmoHeight;
        Handles.DrawWireDisc(target, Vector3.up, markerRadius);

        foreach (Waypoint approach in sideApproachWaypoints)
        {
            if (approach == null)
                continue;

            Handles.DrawAAPolyLine(
                lineWidth,
                approach.transform.position + Vector3.up * gizmoHeight,
                target
            );
        }

        if (mainlinePreviousWaypoint != null)
        {
            Handles.DrawAAPolyLine(
                lineWidth,
                mainlinePreviousWaypoint.transform.position + Vector3.up * gizmoHeight,
                target
            );
        }

        if (releaseWaypoint != null)
        {
            Handles.DrawAAPolyLine(
                lineWidth,
                target,
                releaseWaypoint.transform.position + Vector3.up * gizmoHeight
            );
        }
#endif
    }
}
