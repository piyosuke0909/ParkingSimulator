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

        if (!EnsureGraph() || roadGraph.nodes.Count == 0)
        {
            Transform approachPoint = targetSlot.GetApproachPoint();
            if (includeStartPosition)
            {
                route.Add(startPosition);
            }

            route.Add(approachPoint.position);
            return route;
        }

        RoadNode startNode = ResolveStartNode(startPosition);
        RoadNode targetNode = ResolveSlotNode(targetSlot);
        Vector3 approachPosition = ResolveSlotApproachPosition(targetSlot, targetNode);

        if (startNode == null || targetNode == null)
        {
            if (includeStartPosition)
            {
                route.Add(startPosition);
            }

            route.Add(approachPosition);
            return route;
        }

        if (includeStartPosition)
        {
            route.Add(startPosition);
        }

        List<Vector3> pathPoints = roadGraph.FindPathPoints(startNode, targetNode);
        foreach (Vector3 point in pathPoints)
        {
            route.Add(point);
        }

        if (route.Count == 0 || Vector3.Distance(route[route.Count - 1], approachPosition) > 0.1f)
        {
            route.Add(approachPosition);
        }

        return route;
    }

    public List<Vector3> BuildRouteToNode(Vector3 startPosition, RoadNode targetNode, bool includeStartPosition = false)
    {
        List<Vector3> route = new List<Vector3>();

        if (targetNode == null)
        {
            Debug.LogWarning($"{name}: Target road node is not set.");
            return route;
        }

        if (!EnsureGraph() || roadGraph.nodes.Count == 0)
        {
            if (includeStartPosition)
            {
                route.Add(startPosition);
            }

            route.Add(targetNode.Position);
            return route;
        }

        RoadNode startNode = roadGraph.GetNearestNode(startPosition);
        if (startNode == null)
        {
            if (includeStartPosition)
            {
                route.Add(startPosition);
            }

            route.Add(targetNode.Position);
            return route;
        }

        if (includeStartPosition)
        {
            route.Add(startPosition);
        }

        List<Vector3> pathPoints = roadGraph.FindPathPoints(startNode, targetNode);
        foreach (Vector3 point in pathPoints)
        {
            route.Add(point);
        }

        if (route.Count == 0 || Vector3.Distance(route[route.Count - 1], targetNode.Position) > 0.1f)
        {
            route.Add(targetNode.Position);
        }

        return route;
    }

    public List<Vector3> BuildRouteBetweenNodes(RoadNode startNode, RoadNode targetNode)
    {
        List<Vector3> route = new List<Vector3>();

        if (startNode == null || targetNode == null)
        {
            return route;
        }

        if (!EnsureGraph() || roadGraph.nodes.Count == 0)
        {
            route.Add(targetNode.Position);
            return route;
        }

        List<Vector3> pathPoints = roadGraph.FindPathPoints(startNode, targetNode);
        foreach (Vector3 point in pathPoints)
        {
            route.Add(point);
        }

        if (route.Count == 0 || Vector3.Distance(route[route.Count - 1], targetNode.Position) > 0.1f)
        {
            route.Add(targetNode.Position);
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

        if ((targetSlot.slotId == "C-01" || targetSlot.name == "Slot_C_01")
            && roadGraph.TryGetNode("Lane_C_01_Aisle", out RoadNode demoSlotNode))
        {
            return demoSlotNode;
        }

        return roadGraph.GetNearestNode(targetSlot.GetApproachPoint().position);
    }

    private Vector3 ResolveSlotApproachPosition(ParkingSlot targetSlot, RoadNode targetNode)
    {
        Transform parkingPoint = targetSlot.GetParkingPoint();
        Transform approachPoint = targetSlot.GetApproachPoint();
        Vector3 approachPosition = approachPoint.position;

        Vector3 fromParking = approachPosition - parkingPoint.position;
        fromParking.y = 0f;
        if (fromParking.sqrMagnitude > 0.001f)
        {
            return approachPosition;
        }

        return targetNode != null ? targetNode.Position : approachPosition;
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
