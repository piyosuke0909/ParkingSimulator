using UnityEngine;

public enum RoadLaneSide
{
    Center,
    LeftHandTraffic,
    RightHandTraffic
}

public class RoadEdge : MonoBehaviour
{
    [Header("Identity")]
    public string edgeId;
    public string fromNodeId;
    public string toNodeId;

    [Header("Rules")]
    public bool oneWay = true;
    public bool blocked;
    public float speedLimit = 5f;
    public float costMultiplier = 1f;
    public RoadLaneSide laneSide = RoadLaneSide.Center;
    public float laneWidth = 6f;
    public float roadWidth = 12f;
    public float turnRadius = 4f;

    [Header("Shape")]
    public Vector3[] pathPoints = new Vector3[0];

    public bool Matches(RoadNode fromNode, RoadNode toNode, bool reverseAllowed, out bool reverse)
    {
        reverse = false;
        if (fromNode == null || toNode == null || blocked)
        {
            return false;
        }

        if (fromNode.nodeId == fromNodeId && toNode.nodeId == toNodeId)
        {
            return true;
        }

        if (reverseAllowed && !oneWay && fromNode.nodeId == toNodeId && toNode.nodeId == fromNodeId)
        {
            reverse = true;
            return true;
        }

        return false;
    }

    public float GetTraversalCost(RoadNode fromNode, RoadNode toNode)
    {
        return GetDistance(fromNode, toNode) * Mathf.Max(0.01f, costMultiplier);
    }

    public float GetDistance(RoadNode fromNode, RoadNode toNode)
    {
        if (fromNode == null || toNode == null)
        {
            return 0f;
        }

        float distance = 0f;
        Vector3 previous = fromNode.Position;

        if (pathPoints != null)
        {
            foreach (Vector3 point in pathPoints)
            {
                distance += Vector3.Distance(previous, point);
                previous = point;
            }
        }

        distance += Vector3.Distance(previous, toNode.Position);
        return distance;
    }

    private void Reset()
    {
        if (string.IsNullOrWhiteSpace(edgeId))
        {
            edgeId = gameObject.name;
        }
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(edgeId))
        {
            edgeId = gameObject.name;
        }

        speedLimit = Mathf.Max(0.01f, speedLimit);
        costMultiplier = Mathf.Max(0.01f, costMultiplier);
        laneWidth = Mathf.Max(0.01f, laneWidth);
        roadWidth = Mathf.Max(laneWidth, roadWidth);
        turnRadius = Mathf.Max(0.01f, turnRadius);
    }

    private void OnDrawGizmosSelected()
    {
        if (pathPoints == null || pathPoints.Length == 0)
        {
            return;
        }

        Gizmos.color = blocked ? Color.red : Color.cyan;
        for (int i = 0; i < pathPoints.Length; i++)
        {
            Gizmos.DrawSphere(pathPoints[i], 0.35f);
            if (i > 0)
            {
                Gizmos.DrawLine(pathPoints[i - 1], pathPoints[i]);
            }
        }
    }
}
