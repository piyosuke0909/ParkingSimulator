using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 親GameObject配下のWaypointへ、Mainline・ConnectorLane・ParkingPointとLane Group IDを一括設定します。
/// Editor専用の補助です。
/// </summary>
public class WaypointTrafficRoleAssigner :
    MonoBehaviour
{
    public WaypointTrafficRole role =
        WaypointTrafficRole.Mainline;

    public int laneGroupId;

    [ContextMenu("Apply To Child Waypoints")]
    public void ApplyToChildWaypoints()
    {
        Waypoint[] waypoints =
            GetComponentsInChildren<
                Waypoint>(
                true
            );

        foreach (Waypoint waypoint
                 in waypoints)
        {
            if (waypoint == null)
            {
                continue;
            }

#if UNITY_EDITOR
            Undo.RecordObject(
                waypoint,
                "Assign Waypoint Traffic Role"
            );
#endif

            waypoint.trafficRole = role;
            waypoint.laneGroupId =
                laneGroupId;

#if UNITY_EDITOR
            EditorUtility.SetDirty(
                waypoint
            );
#endif
        }

        Debug.Log(
            $"{name}: {waypoints.Length} Waypointsへ"
            + $" Role={role},"
            + $" LaneGroup={laneGroupId}を設定しました。",
            this
        );
    }
}
