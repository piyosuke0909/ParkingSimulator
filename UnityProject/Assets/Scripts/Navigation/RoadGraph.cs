using System.Collections.Generic;
using UnityEngine;

public class RoadGraph : MonoBehaviour
{
    [Header("Nodes")]
    public Transform nodesRoot;
    public List<RoadNode> nodes = new List<RoadNode>();
    public Transform edgesRoot;
    public List<RoadEdge> edges = new List<RoadEdge>();
    public bool autoCollectOnAwake = true;
    public bool treatConnectionsAsBidirectional = true;
    public bool useRoadEdgesWhenAvailable = true;

    private void Awake()
    {
        if (autoCollectOnAwake)
        {
            AutoCollect();
        }
    }

    [ContextMenu("Auto Collect")]
    public void AutoCollect()
    {
        AutoCollectNodes();
        AutoCollectEdges();
    }

    [ContextMenu("Auto Collect Nodes")]
    public void AutoCollectNodes()
    {
        Transform root = nodesRoot != null ? nodesRoot : transform;
        nodes.Clear();
        nodes.AddRange(root.GetComponentsInChildren<RoadNode>());
    }

    [ContextMenu("Auto Collect Edges")]
    public void AutoCollectEdges()
    {
        Transform root = edgesRoot != null ? edgesRoot : transform;
        edges.Clear();
        edges.AddRange(root.GetComponentsInChildren<RoadEdge>());
    }

    public bool TryGetNode(string nodeId, out RoadNode node)
    {
        node = null;

        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return false;
        }

        foreach (RoadNode candidate in nodes)
        {
            if (candidate != null && candidate.nodeId == nodeId)
            {
                node = candidate;
                return true;
            }
        }

        return false;
    }

    public RoadNode GetNearestNode(Vector3 position)
    {
        RoadNode nearestNode = null;
        float nearestDistance = float.MaxValue;

        foreach (RoadNode node in nodes)
        {
            if (node == null)
            {
                continue;
            }

            float distance = Vector3.SqrMagnitude(node.Position - position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestNode = node;
            }
        }

        return nearestNode;
    }

    public List<RoadNode> FindPath(RoadNode startNode, RoadNode goalNode)
    {
        List<RoadNode> emptyPath = new List<RoadNode>();

        if (startNode == null || goalNode == null)
        {
            return emptyPath;
        }

        if (startNode == goalNode)
        {
            emptyPath.Add(startNode);
            return emptyPath;
        }

        HashSet<RoadNode> unvisited = new HashSet<RoadNode>();
        Dictionary<RoadNode, float> distanceByNode = new Dictionary<RoadNode, float>();
        Dictionary<RoadNode, RoadNode> previousByNode = new Dictionary<RoadNode, RoadNode>();

        foreach (RoadNode node in nodes)
        {
            if (node == null)
            {
                continue;
            }

            unvisited.Add(node);
            distanceByNode[node] = float.MaxValue;
        }

        if (!distanceByNode.ContainsKey(startNode))
        {
            unvisited.Add(startNode);
            distanceByNode[startNode] = float.MaxValue;
        }

        distanceByNode[startNode] = 0f;

        while (unvisited.Count > 0)
        {
            RoadNode currentNode = GetClosestUnvisitedNode(unvisited, distanceByNode);
            if (currentNode == null)
            {
                break;
            }

            if (currentNode == goalNode)
            {
                return ReconstructPath(previousByNode, currentNode);
            }

            unvisited.Remove(currentNode);

            foreach (RoadNeighbor neighbor in GetNeighbors(currentNode))
            {
                if (neighbor.node == null || !unvisited.Contains(neighbor.node))
                {
                    continue;
                }

                float edgeCost = neighbor.cost;
                float candidateDistance = distanceByNode[currentNode] + edgeCost;

                if (candidateDistance < distanceByNode[neighbor.node])
                {
                    distanceByNode[neighbor.node] = candidateDistance;
                    previousByNode[neighbor.node] = currentNode;
                }
            }
        }

        return emptyPath;
    }

    public List<Vector3> FindPathPoints(RoadNode startNode, RoadNode goalNode)
    {
        List<Vector3> route = new List<Vector3>();
        List<RoadNode> nodePath = FindPath(startNode, goalNode);

        if (nodePath.Count == 0)
        {
            return route;
        }

        if (nodePath.Count == 1)
        {
            AddRoutePoint(route, nodePath[0].Position);
            return route;
        }

        for (int i = 0; i < nodePath.Count - 1; i++)
        {
            AppendSegmentPoints(route, nodePath[i], nodePath[i + 1]);
        }

        return route;
    }

    private RoadNode GetClosestUnvisitedNode(HashSet<RoadNode> unvisited, Dictionary<RoadNode, float> distanceByNode)
    {
        RoadNode closestNode = null;
        float closestDistance = float.MaxValue;

        foreach (RoadNode node in unvisited)
        {
            float distance = distanceByNode.ContainsKey(node) ? distanceByNode[node] : float.MaxValue;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestNode = node;
            }
        }

        return closestNode;
    }

    private IEnumerable<RoadNeighbor> GetNeighbors(RoadNode node)
    {
        if (useRoadEdgesWhenAvailable && edges.Count > 0)
        {
            foreach (RoadEdge edge in edges)
            {
                if (TryGetEdgeNeighbor(node, edge, out RoadNode edgeNeighbor, out float edgeCost))
                {
                    yield return new RoadNeighbor(edgeNeighbor, edgeCost);
                }
            }

            yield break;
        }

        foreach (RoadNode connectedNode in node.connectedNodes)
        {
            if (connectedNode != null)
            {
                float cost = Vector3.Distance(node.Position, connectedNode.Position) * connectedNode.traversalCost;
                yield return new RoadNeighbor(connectedNode, cost);
            }
        }

        if (!treatConnectionsAsBidirectional)
        {
            yield break;
        }

        foreach (RoadNode candidate in nodes)
        {
            if (candidate != null && candidate.connectedNodes.Contains(node))
            {
                float cost = Vector3.Distance(node.Position, candidate.Position) * candidate.traversalCost;
                yield return new RoadNeighbor(candidate, cost);
            }
        }
    }

    private bool TryGetEdgeNeighbor(RoadNode node, RoadEdge edge, out RoadNode neighbor, out float cost)
    {
        neighbor = null;
        cost = 0f;

        if (node == null || edge == null || edge.blocked)
        {
            return false;
        }

        if (node.nodeId == edge.fromNodeId && TryGetNode(edge.toNodeId, out neighbor))
        {
            cost = edge.GetTraversalCost(node, neighbor);
            return true;
        }

        if (!edge.oneWay && node.nodeId == edge.toNodeId && TryGetNode(edge.fromNodeId, out neighbor))
        {
            cost = edge.GetTraversalCost(node, neighbor);
            return true;
        }

        return false;
    }

    private void AppendSegmentPoints(List<Vector3> route, RoadNode fromNode, RoadNode toNode)
    {
        AddRoutePoint(route, fromNode.Position);

        RoadEdge edge = FindEdge(fromNode, toNode, out bool reverse);
        if (edge != null && edge.pathPoints != null && edge.pathPoints.Length > 0)
        {
            if (reverse)
            {
                for (int i = edge.pathPoints.Length - 1; i >= 0; i--)
                {
                    AddRoutePoint(route, edge.pathPoints[i]);
                }
            }
            else
            {
                for (int i = 0; i < edge.pathPoints.Length; i++)
                {
                    AddRoutePoint(route, edge.pathPoints[i]);
                }
            }
        }

        AddRoutePoint(route, toNode.Position);
    }

    private RoadEdge FindEdge(RoadNode fromNode, RoadNode toNode, out bool reverse)
    {
        reverse = false;
        foreach (RoadEdge edge in edges)
        {
            if (edge != null && edge.Matches(fromNode, toNode, treatConnectionsAsBidirectional, out reverse))
            {
                return edge;
            }
        }

        return null;
    }

    private void AddRoutePoint(List<Vector3> route, Vector3 point)
    {
        if (route.Count > 0 && Vector3.Distance(route[route.Count - 1], point) <= 0.1f)
        {
            return;
        }

        route.Add(point);
    }

    private List<RoadNode> ReconstructPath(Dictionary<RoadNode, RoadNode> previousByNode, RoadNode currentNode)
    {
        List<RoadNode> path = new List<RoadNode>();
        path.Add(currentNode);

        while (previousByNode.ContainsKey(currentNode))
        {
            currentNode = previousByNode[currentNode];
            path.Add(currentNode);
        }

        path.Reverse();
        return path;
    }

    private struct RoadNeighbor
    {
        public readonly RoadNode node;
        public readonly float cost;

        public RoadNeighbor(RoadNode node, float cost)
        {
            this.node = node;
            this.cost = cost;
        }
    }
}
