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

    [Header("Road Sections")]
    [Tooltip("nextWaypoints Ç∆ìØÇ∂èáî‘Ç≈ÅAÇªÇ±Ç÷å¸Ç©Ç§ìπÇÃó\ñÒëŒè€Çê›íËÇµÇ‹Ç∑ÅBëoï˚å¸í òHÇ≈ÇÕîΩëŒï˚å¸Ç…Ç‡ìØÇ∂RoadSectionÇê›íËÇµÇ‹Ç∑ÅB")]
    public List<RoadSection> roadSectionsToNext = new List<RoadSection>();

    [Header("Gizmo Arrow Settings")]
    public float arrowHeadLength = 1.2f;
    public float arrowHeadAngle = 25f;
    public float arrowOffsetFromTarget = 0.8f;

    public NPC_CarController reservedBy;
    public NPC_CarController occupiedBy;

    public bool TryReserve(NPC_CarController car)
    {
        if (car == null)
        {
            return false;
        }

        if (reservedBy == null || reservedBy == car)
        {
            reservedBy = car;
            return true;
        }

        return false;
    }

    public void Enter(NPC_CarController car)
    {
        if (car == null)
        {
            return;
        }

        if (reservedBy == car)
        {
            reservedBy = null;
        }

        occupiedBy = car;
    }

    public void Release(NPC_CarController car)
    {
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
        return reservedBy == null ||
               reservedBy == car;
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
