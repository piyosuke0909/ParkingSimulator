using System.Collections.Generic;
using UnityEngine;

public class RoadGraph : MonoBehaviour
{
    [Header("Nodes")]
    public Transform nodesRoot;
    public List<RoadNode> nodes = new List<RoadNode>();
    public bool autoCollectOnAwake = true;
    public bool treatConnectionsAsBidirectional = true;

    private void Awake()
    {
        if (autoCollectOnAwake)
        {
            AutoCollectNodes();
        }
    }

    [ContextMenu("Auto Collect Nodes")]
    public void AutoCollectNodes()
    {
        Transform root = nodesRoot != null ? nodesRoot : transform;
        nodes.Clear();
        nodes.AddRange(root.GetComponentsInChildren<RoadNode>());
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

            foreach (RoadNode neighbor in GetNeighbors(currentNode))
            {
                if (neighbor == null || !unvisited.Contains(neighbor))
                {
                    continue;
                }

                float edgeCost = Vector3.Distance(currentNode.Position, neighbor.Position) * neighbor.traversalCost;
                float candidateDistance = distanceByNode[currentNode] + edgeCost;

                if (candidateDistance < distanceByNode[neighbor])
                {
                    distanceByNode[neighbor] = candidateDistance;
                    previousByNode[neighbor] = currentNode;
                }
            }
        }

        return emptyPath;
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

    private IEnumerable<RoadNode> GetNeighbors(RoadNode node)
    {
        foreach (RoadNode connectedNode in node.connectedNodes)
        {
            if (connectedNode != null)
            {
                yield return connectedNode;
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
                yield return candidate;
            }
        }
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
}
