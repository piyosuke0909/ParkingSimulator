using System.Collections.Generic;
using UnityEngine;

public class Waypoint : MonoBehaviour
{
    [Header("Waypoint Info")]
    public string waypointId;

    [Header("Connections")]
    public List<Waypoint> nextWaypoints = new List<Waypoint>();

    [Header("Settings")]
    public bool isStopPoint;
    public bool isIntersection;
    public bool isEntrance;
    public bool isExit;

    [Header("Traffic Block")]
    [Tooltip("このWaypointが属する危険エリアです。同時に入れたくないWaypoint同士には同じTrafficBlockを設定します。未設定なら通常Waypointとして扱います。")]
    public TrafficBlock trafficBlock;

    [Header("Road Sections")]
    [Tooltip("nextWaypoints と同じ順番で、そこへ向かう道の予約対象を設定します。双方向通路では反対方向にも同じRoadSectionを設定します。")]
    public List<RoadSection> roadSectionsToNext = new List<RoadSection>();

    [Header("Gizmo Arrow Settings")]
    public float arrowHeadLength = 1.2f;
    public float arrowHeadAngle = 25f;
    public float arrowOffsetFromTarget = 0.8f;

    [Header("Reservation State")]
    public NPC_CarController reservedBy;
    public NPC_CarController occupiedBy;

    public bool TryReserve(NPC_CarController car)
    {
        if (car == null)
        {
            return false;
        }

        if (occupiedBy != null && occupiedBy != car)
        {
            return false;
        }

        if (reservedBy != null && reservedBy != car)
        {
            return false;
        }

        reservedBy = car;
        return true;
    }

    public void Enter(NPC_CarController car)
    {
        if (car == null)
        {
            return;
        }

        if (occupiedBy != null && occupiedBy != car)
        {
            Debug.LogWarning(
                $"{name}: occupiedBy が別の車のため Enter を拒否しました。NewCar={car.name}, OccupiedBy={occupiedBy.name}",
                this
            );
            return;
        }

        occupiedBy = car;

        if (reservedBy == car)
        {
            reservedBy = null;
        }
    }

    public void Release(NPC_CarController car)
    {
        if (car == null)
        {
            return;
        }

        if (reservedBy == car)
        {
            reservedBy = null;
        }

        if (occupiedBy == car)
        {
            occupiedBy = null;
        }
    }

    public bool IsAvailableFor(NPC_CarController car)
    {
        if (car == null)
        {
            return false;
        }

        if (occupiedBy != null && occupiedBy != car)
        {
            return false;
        }

        if (reservedBy != null && reservedBy != car)
        {
            return false;
        }

        return true;
    }

    public RoadSection GetRoadSectionTo(Waypoint nextWaypoint)
    {
        if (nextWaypoint == null)
        {
            return null;
        }

        int index = nextWaypoints.IndexOf(nextWaypoint);

        if (index < 0)
        {
            return null;
        }

        if (roadSectionsToNext == null)
        {
            return null;
        }

        if (index >= roadSectionsToNext.Count)
        {
            return null;
        }

        return roadSectionsToNext[index];
    }

    private void OnDrawGizmos()
    {
        DrawSelfGizmo();
        DrawNextWaypointGizmos();
    }

    private void DrawSelfGizmo()
    {
        if (isEntrance)
        {
            Gizmos.color = Color.green;
        }
        else if (isExit)
        {
            Gizmos.color = Color.red;
        }
        else if (isIntersection)
        {
            Gizmos.color = Color.yellow;
        }
        else if (isStopPoint)
        {
            Gizmos.color = Color.white;
        }
        else if (trafficBlock != null)
        {
            Gizmos.color = Color.magenta;
        }
        else
        {
            Gizmos.color = Color.cyan;
        }

        Gizmos.DrawSphere(transform.position, 0.5f);
    }

    private void DrawNextWaypointGizmos()
    {
        if (nextWaypoints == null)
        {
            return;
        }

        foreach (Waypoint next in nextWaypoints)
        {
            if (next == null)
            {
                continue;
            }

            DrawArrowLine(transform.position, next.transform.position, Color.yellow);
        }
    }

    private void DrawArrowLine(Vector3 from, Vector3 to, Color color)
    {
        Gizmos.color = color;
        Gizmos.DrawLine(from, to);

        Vector3 direction = (to - from).normalized;
        float distance = Vector3.Distance(from, to);

        if (distance <= 0.01f)
        {
            return;
        }

        Vector3 arrowTip = to - direction * arrowOffsetFromTarget;

        Quaternion rightRotation = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 + arrowHeadAngle, 0);
        Quaternion leftRotation = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 - arrowHeadAngle, 0);

        Vector3 right = arrowTip + (rightRotation * Vector3.forward) * arrowHeadLength;
        Vector3 left = arrowTip + (leftRotation * Vector3.forward) * arrowHeadLength;

        Gizmos.DrawLine(arrowTip, right);
        Gizmos.DrawLine(arrowTip, left);
    }
}
