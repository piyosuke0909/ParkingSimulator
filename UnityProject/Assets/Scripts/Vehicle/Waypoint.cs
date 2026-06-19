using System.Collections.Generic;
using UnityEngine;

public class Waypoint : MonoBehaviour
{
    [Header("Waypoint Info")]
    public string waypointId;

    [Header("Connections")]
    public List<Waypoint> nextWaypoints = new List<Waypoint>();

    [Header("Traffic Zone")]
    [Tooltip("このWaypointへ向かうときに予約するTrafficZone")]
    public TrafficZone trafficZoneToEnter;

    [Tooltip("TrafficZoneが使用中の場合、このWaypointへ進まず手前で待機する")]
    public bool waitBeforeTrafficZone;

    [Tooltip("このWaypointに到着したとき、現在予約中のTrafficZoneを解放する")]
    public bool releaseTrafficZoneHere;

    [Tooltip("このWaypointからTrafficZoneを予約する車を、通常キューより優先します。")]
    public bool isPriorityTrafficZoneEntry;

    [Header("Settings")]
    public bool isStopPoint;
    public bool isIntersection;
    public bool isEntrance;
    public bool isExit;

    [Header("Gizmo Arrow Settings")]
    public float arrowHeadLength = 1.2f;
    public float arrowHeadAngle = 25f;
    public float arrowOffsetFromTarget = 0.8f;

    private void OnDrawGizmos()
    {
        DrawSelfGizmo();
        DrawNextWaypointGizmos();
        DrawTrafficZoneGizmo();
    }

    private void DrawSelfGizmo()
    {
        if (releaseTrafficZoneHere)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(transform.position, 0.65f);
            return;
        }

        if (trafficZoneToEnter != null && waitBeforeTrafficZone)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawSphere(transform.position, 0.65f);
            return;
        }

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

    private void DrawTrafficZoneGizmo()
    {
        if (trafficZoneToEnter == null)
        {
            return;
        }

        Gizmos.color = Color.blue;
        Gizmos.DrawLine(transform.position, trafficZoneToEnter.transform.position);
    }

    private void DrawArrowLine(Vector3 from, Vector3 to, Color color)
    {
        Gizmos.color = color;

        // 本体の線
        Gizmos.DrawLine(from, to);

        Vector3 direction = (to - from).normalized;
        float distance = Vector3.Distance(from, to);

        if (distance <= 0.01f)
        {
            return;
        }

        // 矢印の根元位置（Waypointの球と重なりすぎないよう少し手前）
        Vector3 arrowTip = to - direction * arrowOffsetFromTarget;

        // 矢印の左右の線を作る
        Quaternion rightRotation = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 + arrowHeadAngle, 0);
        Quaternion leftRotation  = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 - arrowHeadAngle, 0);

        Vector3 right = arrowTip + (rightRotation * Vector3.forward) * arrowHeadLength;
        Vector3 left  = arrowTip + (leftRotation  * Vector3.forward) * arrowHeadLength;

        Gizmos.DrawLine(arrowTip, right);
        Gizmos.DrawLine(arrowTip, left);
    }
}