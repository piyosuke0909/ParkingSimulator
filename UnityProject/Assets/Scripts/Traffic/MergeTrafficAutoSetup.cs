using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// WaypointのTraffic RoleとnextWaypointsからMergePointを自動生成します。
///
/// 検出条件:
/// from.trafficRole == ConnectorLane
/// target.trafficRole == Mainline
///
/// 自動設定:
/// ・Side Approach Waypoints
/// ・Merge Target
/// ・Mainline Previous Waypoint
/// ・Release Waypoint
/// ・MergeTrafficCoordinatorへの登録
///
/// Waypoint自体や接続は生成しません。
/// </summary>
public class MergeTrafficAutoSetup : MonoBehaviour
{
    [Header("References")]
    [Tooltip("全Waypointの親です。未設定ならシーン全体を検索します。")]
    public Transform waypointRoot;

    [Tooltip("MergePointを生成する親です。未設定ならこのGameObject配下へ生成します。")]
    public Transform generatedMergeRoot;

    [Tooltip("登録先Coordinatorです。未設定ならシーン内検索します。")]
    public MergeTrafficCoordinator coordinator;

    [Header("Generation")]
    [Tooltip("自動生成したMergePoint名の接頭辞です。")]
    public string generatedNamePrefix =
        "AutoMerge_";

    [Tooltip("既存の自動生成MergePointを削除して再生成します。")]
    public bool replacePreviouslyGenerated = true;

    [Tooltip("同じMerge Targetへ複数の駐車レーン出口が接続する場合、1つのMergePointへ統合します。")]
    public bool combineApproachesByTarget = true;

    [Tooltip("本線直前Waypointを空き確認へ使用します。")]
    public bool requireMainlinePreviousClear = true;

    [Tooltip("Merge Targetの次の本線Waypointを先取りします。")]
    public bool reserveReleaseWaypoint = true;

    [Tooltip("本線の前後Waypoint選択時、同じLane Group IDを優先します。")]
    public bool preferSameLaneGroup = true;

    [Header("Validation")]
    [TextArea(6, 30)]
    public string lastResult;

    private readonly Dictionary<
        Waypoint,
        List<Waypoint>>
        incomingByWaypoint =
            new Dictionary<
                Waypoint,
                List<Waypoint>>();

    [ContextMenu("Generate Merge Points")]
    public void GenerateMergePoints()
    {
        Waypoint[] waypoints =
            CollectWaypoints();

        BuildIncomingLookup(waypoints);

        Transform root =
            generatedMergeRoot != null
                ? generatedMergeRoot
                : transform;

        if (replacePreviouslyGenerated)
        {
            RemovePreviouslyGenerated(root);
        }

        Dictionary<Waypoint, MergePoint>
            pointByTarget =
                new Dictionary<
                    Waypoint,
                    MergePoint>();

        List<string> errors =
            new List<string>();

        int generatedCount = 0;
        int approachCount = 0;

        foreach (Waypoint from in waypoints)
        {
            if (from == null ||
                from.trafficRole !=
                    WaypointTrafficRole.ConnectorLane ||
                from.nextWaypoints == null)
            {
                continue;
            }

            foreach (Waypoint target
                     in from.nextWaypoints)
            {
                if (target == null ||
                    target.trafficRole !=
                        WaypointTrafficRole.Mainline ||
                    from.trafficRole ==
                        WaypointTrafficRole.ParkingPoint)
                {
                    continue;
                }

                MergePoint point = null;

                if (combineApproachesByTarget)
                {
                    pointByTarget.TryGetValue(
                        target,
                        out point
                    );
                }

                if (point == null)
                {
                    point =
                        CreateMergePoint(
                            root,
                            target
                        );

                    generatedCount++;

                    if (combineApproachesByTarget)
                    {
                        pointByTarget[target] =
                            point;
                    }
                }

                if (!point.sideApproachWaypoints
                    .Contains(from))
                {
                    point.sideApproachWaypoints
                        .Add(from);

                    approachCount++;
                }

                if (point.mainlinePreviousWaypoint ==
                    null)
                {
                    point.mainlinePreviousWaypoint =
                        FindMainlinePrevious(
                            target
                        );
                }

                if (point.releaseWaypoint == null)
                {
                    point.releaseWaypoint =
                        FindMainlineNext(
                            target
                        );
                }

                if (point.mainlinePreviousWaypoint ==
                    null)
                {
                    errors.Add(
                        $"{point.name}: "
                        + $"{target.name}の本線直前Waypointを"
                        + "検出できません。"
                    );
                }

                if (point.releaseWaypoint == null)
                {
                    errors.Add(
                        $"{point.name}: "
                        + $"{target.name}の本線次Waypointを"
                        + "検出できません。"
                    );
                }
            }
        }

        MergeTrafficCoordinator activeCoordinator =
            ResolveCoordinator();

        if (activeCoordinator != null)
        {
            activeCoordinator.CollectMergePoints();
            activeCoordinator.RebuildLookup();
        }
        else
        {
            errors.Add(
                "MergeTrafficCoordinatorが見つかりません。"
            );
        }

        lastResult =
            $"Generated MergePoints={generatedCount}, "
            + $"Approaches={approachCount}, "
            + $"Errors={errors.Count}";

        if (errors.Count > 0)
        {
            lastResult +=
                "\n- " +
                string.Join(
                    "\n- ",
                    errors
                );

            Debug.LogWarning(
                lastResult,
                this
            );
        }
        else
        {
            Debug.Log(
                lastResult,
                this
            );
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif
    }

    [ContextMenu("Validate Traffic Roles")]
    public void ValidateTrafficRoles()
    {
        Waypoint[] waypoints =
            CollectWaypoints();

        List<string> errors =
            new List<string>();

        int mainlineCount = 0;
        int connectorCount = 0;
        int parkingPointCount = 0;
        int unspecifiedCount = 0;
        int mergeEdgeCount = 0;

        foreach (Waypoint waypoint
                 in waypoints)
        {
            if (waypoint == null)
            {
                continue;
            }

            if (waypoint.trafficRole ==
                WaypointTrafficRole.Mainline)
            {
                mainlineCount++;
            }
            else if (waypoint.trafficRole ==
                     WaypointTrafficRole.ConnectorLane)
            {
                connectorCount++;
            }
            else if (waypoint.trafficRole ==
                     WaypointTrafficRole.ParkingPoint)
            {
                parkingPointCount++;
            }
            else
            {
                unspecifiedCount++;
            }

            if (waypoint.nextWaypoints == null)
            {
                continue;
            }

            if (waypoint.trafficRole ==
                    WaypointTrafficRole.ParkingPoint &&
                waypoint.nextWaypoints.Count > 0)
            {
                errors.Add(
                    $"{waypoint.name}: ParkingPointに"
                    + "nextWaypointsがあります。"
                    + " 駐車位置を通過経路にする場合は"
                    + "ConnectorLaneへ変更してください。"
                );
            }

            foreach (Waypoint next
                     in waypoint.nextWaypoints)
            {
                if (next == null)
                {
                    errors.Add(
                        $"{waypoint.name}: "
                        + "nextWaypointsにnullがあります。"
                    );

                    continue;
                }

                if (waypoint.trafficRole ==
                        WaypointTrafficRole.ConnectorLane &&
                    next.trafficRole ==
                        WaypointTrafficRole.Mainline)
                {
                    mergeEdgeCount++;
                }
            }
        }

        lastResult =
            $"Mainline={mainlineCount}, "
            + $"ConnectorLane={connectorCount}, "
            + $"ParkingPoint={parkingPointCount}, "
            + $"Unspecified={unspecifiedCount}, "
            + $"MergeEdges={mergeEdgeCount}, "
            + $"Errors={errors.Count}";

        if (errors.Count > 0)
        {
            lastResult +=
                "\n- " +
                string.Join(
                    "\n- ",
                    errors
                );

            Debug.LogWarning(
                lastResult,
                this
            );
        }
        else
        {
            Debug.Log(
                lastResult,
                this
            );
        }
    }

    private MergePoint CreateMergePoint(
        Transform root,
        Waypoint target)
    {
        GameObject created =
            new GameObject(
                generatedNamePrefix +
                target.name
            );

        created.transform.SetParent(
            root,
            false
        );

        created.transform.position =
            target.transform.position;

        AutoGeneratedMergePoint marker =
            created.AddComponent<
                AutoGeneratedMergePoint>();

        marker.sourceTarget = target;

        MergePoint point =
            created.AddComponent<
                MergePoint>();

        point.mergeId =
            generatedNamePrefix +
            target.name;

        point.mergeTarget = target;
        point.requireMainlinePreviousClear =
            requireMainlinePreviousClear;

        point.reserveReleaseWaypoint =
            reserveReleaseWaypoint;

        return point;
    }

    private Waypoint FindMainlinePrevious(
        Waypoint target)
    {
        if (target == null ||
            !incomingByWaypoint.TryGetValue(
                target,
                out List<Waypoint> incoming))
        {
            return null;
        }

        Waypoint fallback = null;

        foreach (Waypoint candidate
                 in incoming)
        {
            if (candidate == null ||
                candidate.trafficRole !=
                    WaypointTrafficRole.Mainline)
            {
                continue;
            }

            if (preferSameLaneGroup &&
                candidate.laneGroupId ==
                    target.laneGroupId)
            {
                return candidate;
            }

            if (fallback == null)
            {
                fallback = candidate;
            }
        }

        return fallback;
    }

    private Waypoint FindMainlineNext(
        Waypoint target)
    {
        if (target == null ||
            target.nextWaypoints == null)
        {
            return null;
        }

        Waypoint fallback = null;

        foreach (Waypoint candidate
                 in target.nextWaypoints)
        {
            if (candidate == null ||
                candidate.trafficRole !=
                    WaypointTrafficRole.Mainline)
            {
                continue;
            }

            if (preferSameLaneGroup &&
                candidate.laneGroupId ==
                    target.laneGroupId)
            {
                return candidate;
            }

            if (fallback == null)
            {
                fallback = candidate;
            }
        }

        return fallback;
    }

    private void BuildIncomingLookup(
        Waypoint[] waypoints)
    {
        incomingByWaypoint.Clear();

        foreach (Waypoint waypoint
                 in waypoints)
        {
            if (waypoint == null ||
                waypoint.nextWaypoints == null)
            {
                continue;
            }

            foreach (Waypoint next
                     in waypoint.nextWaypoints)
            {
                if (next == null)
                {
                    continue;
                }

                if (!incomingByWaypoint
                    .TryGetValue(
                        next,
                        out List<Waypoint> incoming))
                {
                    incoming =
                        new List<Waypoint>();

                    incomingByWaypoint[next] =
                        incoming;
                }

                if (!incoming.Contains(
                        waypoint))
                {
                    incoming.Add(
                        waypoint
                    );
                }
            }
        }
    }

    private Waypoint[] CollectWaypoints()
    {
        if (waypointRoot != null)
        {
            return
                waypointRoot
                    .GetComponentsInChildren<
                        Waypoint>(
                        true
                    );
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
                Waypoint>(
                true
            );
#endif
    }

    private MergeTrafficCoordinator
        ResolveCoordinator()
    {
        if (coordinator != null)
        {
            return coordinator;
        }

#if UNITY_2023_1_OR_NEWER
        coordinator =
            FindFirstObjectByType<
                MergeTrafficCoordinator>();
#else
        coordinator =
            FindObjectOfType<
                MergeTrafficCoordinator>();
#endif

        return coordinator;
    }

    private static void RemovePreviouslyGenerated(
        Transform root)
    {
        if (root == null)
        {
            return;
        }

        AutoGeneratedMergePoint[] generated =
            root.GetComponentsInChildren<
                AutoGeneratedMergePoint>(
                true
            );

        foreach (AutoGeneratedMergePoint marker
                 in generated)
        {
            if (marker == null)
            {
                continue;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.DestroyObjectImmediate(
                    marker.gameObject
                );

                continue;
            }
#endif

            Destroy(marker.gameObject);
        }
    }
}

/// <summary>
/// AutoSetupが生成したMergePointだけを識別するマーカーです。
/// </summary>
public class AutoGeneratedMergePoint :
    MonoBehaviour
{
    public Waypoint sourceTarget;
}
