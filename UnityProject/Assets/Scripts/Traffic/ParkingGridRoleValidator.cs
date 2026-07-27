using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// アップロードされた駐車場マップ構造に合わせた設定検査です。
///
/// 想定:
/// ・横方向のMainlineが6レーン
/// ・各横レーンは28 Waypoint
/// ・縦方向はConnectorLane
/// ・白丸はParkingPoint
///
/// 自動生成・自動修正は行いません。
/// </summary>
public class ParkingGridRoleValidator : MonoBehaviour
{
    public Transform waypointRoot;

    [Tooltip("横方向6レーンの親を登録します。")]
    public List<Transform> mainlineRoots =
        new List<Transform>();

    [Tooltip("縦方向連絡レーンの親を登録します。")]
    public List<Transform> connectorLaneRoots =
        new List<Transform>();

    [Tooltip("白丸の駐車位置をまとめた親を登録します。Waypointが付いている場合だけ検査します。")]
    public List<Transform> parkingPointRoots =
        new List<Transform>();

    [TextArea(8, 40)]
    public string validationResult;

    [ContextMenu("Validate Parking Grid Roles")]
    public void ValidateRoles()
    {
        List<string> errors =
            new List<string>();

        HashSet<int> mainlineGroupIds =
            new HashSet<int>();

        if (mainlineRoots.Count != 6)
        {
            errors.Add(
                $"Mainline Rootsは6個を想定しています。"
                + $" Current={mainlineRoots.Count}"
            );
        }

        foreach (Transform root
                 in mainlineRoots)
        {
            if (root == null)
            {
                errors.Add(
                    "Mainline Rootsにnullがあります。"
                );
                continue;
            }

            Waypoint[] waypoints =
                root.GetComponentsInChildren<
                    Waypoint>(true);

            if (waypoints.Length != 28)
            {
                errors.Add(
                    $"{root.name}: Mainline Waypointは"
                    + $"28個を想定しています。"
                    + $" Current={waypoints.Length}"
                );
            }

            int? laneId = null;

            foreach (Waypoint waypoint
                     in waypoints)
            {
                if (waypoint.trafficRole !=
                    WaypointTrafficRole.Mainline)
                {
                    errors.Add(
                        $"{waypoint.name}: "
                        + "Traffic RoleがMainlineではありません。"
                    );
                }

                if (!laneId.HasValue)
                {
                    laneId =
                        waypoint.laneGroupId;
                }
                else if (laneId.Value !=
                         waypoint.laneGroupId)
                {
                    errors.Add(
                        $"{root.name}: Lane Group IDが"
                        + "統一されていません。"
                    );
                }
            }

            if (laneId.HasValue &&
                !mainlineGroupIds.Add(
                    laneId.Value))
            {
                errors.Add(
                    $"{root.name}: Lane Group ID "
                    + $"{laneId.Value}が他のMainlineと重複しています。"
                );
            }
        }

        foreach (Transform root
                 in connectorLaneRoots)
        {
            if (root == null)
            {
                errors.Add(
                    "Connector Lane Rootsにnullがあります。"
                );
                continue;
            }

            Waypoint[] waypoints =
                root.GetComponentsInChildren<
                    Waypoint>(true);

            foreach (Waypoint waypoint
                     in waypoints)
            {
                if (waypoint.trafficRole !=
                    WaypointTrafficRole.ConnectorLane)
                {
                    errors.Add(
                        $"{waypoint.name}: "
                        + "Traffic RoleがConnectorLaneではありません。"
                    );
                }
            }
        }

        foreach (Transform root
                 in parkingPointRoots)
        {
            if (root == null)
            {
                continue;
            }

            Waypoint[] waypoints =
                root.GetComponentsInChildren<
                    Waypoint>(true);

            foreach (Waypoint waypoint
                     in waypoints)
            {
                if (waypoint.trafficRole !=
                    WaypointTrafficRole.ParkingPoint)
                {
                    errors.Add(
                        $"{waypoint.name}: "
                        + "Traffic RoleがParkingPointではありません。"
                    );
                }
            }
        }

        Waypoint[] all =
            CollectWaypoints();

        validationResult =
            errors.Count == 0
                ? $"Valid: AllWaypoints={all.Length}, "
                  + $"Mainlines={mainlineRoots.Count}, "
                  + $"ConnectorRoots={connectorLaneRoots.Count}"
                : "Invalid:\n- "
                  + string.Join(
                      "\n- ",
                      errors
                  );

        if (errors.Count == 0)
        {
            Debug.Log(
                validationResult,
                this
            );
        }
        else
        {
            Debug.LogError(
                validationResult,
                this
            );
        }
    }

    private Waypoint[] CollectWaypoints()
    {
        if (waypointRoot != null)
        {
            return
                waypointRoot
                    .GetComponentsInChildren<
                        Waypoint>(true);
        }

#if UNITY_2023_1_OR_NEWER
        return
            FindObjectsByType<
                Waypoint>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );
#else
        return
            FindObjectsOfType<
                Waypoint>(true);
#endif
    }
}
