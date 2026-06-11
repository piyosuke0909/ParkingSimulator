using UnityEngine;

public enum DrivableAreaType
{
    Lane,
    Entrance,
    Exit,
    ParkingApproach,
    Other
}

public class DrivableArea : MonoBehaviour
{
    public string areaId;
    public Vector3[] polygon = new Vector3[0];
    public DrivableAreaType areaType = DrivableAreaType.Lane;
    public float defaultSpeedLimit = 5f;
    public bool allowStop = true;
    public int priority;

    [Header("Debug")]
    public bool drawDebug = true;
    public Color debugColor = new Color(0f, 0.7f, 1f, 1f);

    public bool HasPolygon
    {
        get { return polygon != null && polygon.Length >= 3; }
    }

    public bool ContainsPoint(Vector3 worldPoint)
    {
        if (!HasPolygon)
        {
            return true;
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
        if (collisionShape == null)
        {
            return ContainsPoint(position);
        }

        Vector3[] footprint = collisionShape.GetWorldFootprint(position, rotation, 0f);
        foreach (Vector3 corner in footprint)
        {
            if (!ContainsPoint(corner))
            {
                return false;
            }
        }

        return true;
    }

    private void OnValidate()
    {
        defaultSpeedLimit = Mathf.Max(0.01f, defaultSpeedLimit);
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
