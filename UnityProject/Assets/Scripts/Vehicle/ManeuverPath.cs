using System.Collections.Generic;
using UnityEngine;

public enum ParkingManeuverType
{
    FrontIn,
    ReverseIn,
    MultiPoint
}

public class ManeuverPath : MonoBehaviour
{
    public string pathId;
    public string slotId;
    public ParkingManeuverType maneuverType = ParkingManeuverType.FrontIn;
    public Transform[] controlPoints = new Transform[0];
    public bool requiresReverse;
    public float estimatedDuration = 4f;
    public float requiredClearance = 0.2f;
    public float maxSteeringAngle = 35f;
    public int retryCount;

    public List<Vector3> BuildWorldPoints(ParkingSlot slot)
    {
        List<Vector3> points = new List<Vector3>();

        if (controlPoints != null && controlPoints.Length > 0)
        {
            foreach (Transform point in controlPoints)
            {
                if (point != null)
                {
                    points.Add(point.position);
                }
            }
        }

        if (points.Count > 0)
        {
            return points;
        }

        if (slot == null)
        {
            return points;
        }

        points.Add(slot.GetApproachPoint().position);

        Transform entryPoint = maneuverType == ParkingManeuverType.ReverseIn ? slot.reverseEntryPoint : slot.frontEntryPoint;
        if (entryPoint != null)
        {
            points.Add(entryPoint.position);
        }

        points.Add(slot.GetParkingPoint().position);
        return points;
    }

    private void OnValidate()
    {
        estimatedDuration = Mathf.Max(0.01f, estimatedDuration);
        requiredClearance = Mathf.Max(0f, requiredClearance);
        maxSteeringAngle = Mathf.Clamp(maxSteeringAngle, 0f, 90f);
        retryCount = Mathf.Max(0, retryCount);
        if (maneuverType == ParkingManeuverType.ReverseIn)
        {
            requiresReverse = true;
        }
    }

    private void OnDrawGizmosSelected()
    {
        List<Vector3> points = BuildWorldPoints(GetComponentInParent<ParkingSlot>());
        if (points.Count == 0)
        {
            return;
        }

        Gizmos.color = maneuverType == ParkingManeuverType.ReverseIn ? Color.magenta : Color.green;
        for (int i = 0; i < points.Count; i++)
        {
            Gizmos.DrawSphere(points[i], 0.25f);
            if (i > 0)
            {
                Gizmos.DrawLine(points[i - 1], points[i]);
            }
        }
    }
}
