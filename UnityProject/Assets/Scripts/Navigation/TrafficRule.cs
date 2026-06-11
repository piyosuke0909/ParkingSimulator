using UnityEngine;

public class TrafficRule : MonoBehaviour
{
    public string ruleId;
    public string targetNodeId;
    public string targetEdgeId;
    public bool oneWay;
    public bool stopRequired;
    public bool yieldRequired;
    public float speedLimit = 5f;
    public bool noParking;

    private void OnValidate()
    {
        speedLimit = Mathf.Max(0.01f, speedLimit);
    }
}
