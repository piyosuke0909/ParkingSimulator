using UnityEngine;

public class NoDriveArea : MonoBehaviour
{
    public string areaId;
    public Vector3[] polygon = new Vector3[0];
    public bool active = true;

    [Header("Debug")]
    public bool drawDebug = true;
    public Color debugColor = new Color(1f, 0.15f, 0.05f, 1f);

    public bool HasPolygon
    {
        get { return polygon != null && polygon.Length >= 3; }
    }

    public bool ContainsPoint(Vector3 worldPoint)
    {
        if (!active || !HasPolygon)
        {
            return false;
        }

        bool inside = false;
        int pointCount = polygon.Length;

        for (int i = 0, j = pointCount - 1; i < pointCount; j = i++)
        {
            Vector3 pointA = polygon[i];
            Vector3 pointB = polygon[j];
            bool crossesZ = (pointA.z > worldPoint.z) != (pointB.z > worldPoint.z);

            if (!crossesZ)
            {
                continue;
            }

            float crossingX = (pointB.x - pointA.x) * (worldPoint.z - pointA.z) / (pointB.z - pointA.z) + pointA.x;
            if (worldPoint.x < crossingX)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    public bool ContainsVehiclePose(VehicleCollisionShape collisionShape, Vector3 position, Quaternion rotation)
    {
        if (!active || !HasPolygon)
        {
            return false;
        }

        if (ContainsPoint(position))
        {
            return true;
        }

        if (collisionShape == null)
        {
            return false;
        }

        Vector3[] footprint = collisionShape.GetWorldFootprint(position, rotation, 0f);
        foreach (Vector3 corner in footprint)
        {
            if (ContainsPoint(corner))
            {
                return true;
            }
        }

        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebug || polygon == null || polygon.Length < 2)
        {
            return;
        }

        Gizmos.color = debugColor;
        for (int i = 0; i < polygon.Length; i++)
        {
            Gizmos.DrawLine(polygon[i], polygon[(i + 1) % polygon.Length]);
        }
    }
}
