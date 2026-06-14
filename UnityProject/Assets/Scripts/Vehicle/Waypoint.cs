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

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(transform.position, 0.5f);

        if (nextWaypoints == null)
        {
            return;
        }

        Gizmos.color = Color.yellow;

        foreach (Waypoint next in nextWaypoints)
        {
            if (next == null)
            {
                continue;
            }

            Gizmos.DrawLine(transform.position, next.transform.position);
        }
    }
}