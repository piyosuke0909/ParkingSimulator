using System.Collections.Generic;
using UnityEngine;

public class RoadNode : MonoBehaviour
{
    [Header("Node")]
    public string nodeId;
    public List<RoadNode> connectedNodes = new List<RoadNode>();

    [Header("Cost")]
    public float traversalCost = 1f;
    public float speedLimit = 5f;
    public bool stopAllowed = true;

    public Vector3 Position
    {
        get { return transform.position; }
    }

    private void Reset()
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            nodeId = gameObject.name;
        }
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            nodeId = gameObject.name;
        }

        traversalCost = Mathf.Max(0.01f, traversalCost);
        speedLimit = Mathf.Max(0.01f, speedLimit);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(transform.position, 0.8f);

        Gizmos.color = Color.blue;
        foreach (RoadNode connectedNode in connectedNodes)
        {
            if (connectedNode != null)
            {
                Gizmos.DrawLine(transform.position, connectedNode.transform.position);
            }
        }
    }
}
