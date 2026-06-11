using System.Collections.Generic;
using UnityEngine;

public class RoutePlanner : MonoBehaviour
{
    [Header("Graph")]
    public RoadGraph roadGraph;
    public RoadNode entranceNode;
    public bool useEntranceNodeAsDefaultStart = true;

    private void Awake()
    {
        EnsureGraph();
    }

    public List<Vector3> BuildRouteFromPosition(Vector3 startPosition, ParkingSlot targetSlot, bool includeStartPosition = false)
    {
        List<Vector3> route = new List<Vector3>();

        if (targetSlot == null)
        {
            Debug.LogWarning($"{name}: Target parking slot is not set.");
            return route;
        }

        Transform approachPoint = targetSlot.GetApproachPoint();

        if (!EnsureGraph() || roadGraph.nodes.Count == 0)
        {
            if (includeStartPosition)
            {
                route.Add(startPosition);
            }

            route.Add(approachPoint.position);
            return route;
        }

        RoadNode startNode = ResolveStartNode(startPosition);
        RoadNode targetNode = ResolveSlotNode(targetSlot);

        if (startNode == null || targetNode == null)
        {
            if (includeStartPosition)
            {
                route.Add(startPosition);
            }

            route.Add(approachPoint.position);
            return route;
        }

        if (includeStartPosition)
        {
            route.Add(startPosition);
        }

        List<RoadNode> nodePath = roadGraph.FindPath(startNode, targetNode);
        foreach (RoadNode roadNode in nodePath)
        {
            if (roadNode != null)
            {
                route.Add(roadNode.Position);
            }
        }

        if (route.Count == 0 || Vector3.Distance(route[route.Count - 1], approachPoint.position) > 0.1f)
        {
            route.Add(approachPoint.position);
        }

        return route;
    }

    public RoadNode ResolveSlotNode(ParkingSlot targetSlot)
    {
        if (targetSlot == null || !EnsureGraph())
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(targetSlot.roadNodeId) && roadGraph.TryGetNode(targetSlot.roadNodeId, out RoadNode nodeById))
        {
            return nodeById;
        }

        return roadGraph.GetNearestNode(targetSlot.GetApproachPoint().position);
    }

    private RoadNode ResolveStartNode(Vector3 startPosition)
    {
        if (!EnsureGraph())
        {
            return null;
        }

        if (useEntranceNodeAsDefaultStart && entranceNode != null)
        {
            return entranceNode;
        }

        return roadGraph.GetNearestNode(startPosition);
    }

    private bool EnsureGraph()
    {
        if (roadGraph == null)
        {
            roadGraph = FindObjectOfType<RoadGraph>();
        }

        return roadGraph != null;
    }
}
