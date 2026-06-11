using UnityEngine;

public class ParkingSlotGeometry : MonoBehaviour
{
    [Header("Slot")]
    public string slotId;
    public Vector3[] corners = new Vector3[0];
    public Vector3[] innerPolygon = new Vector3[0];
    public float allowedVehicleMargin = 0.2f;

    [Header("Boundaries")]
    public Vector3[] frontBoundary = new Vector3[0];
    public Vector3[] leftBoundary = new Vector3[0];
    public Vector3[] rightBoundary = new Vector3[0];
    public Vector3[] rearBoundary = new Vector3[0];

    [Header("Debug")]
    public bool drawDebug = true;
    public Color polygonColor = Color.white;

    public bool HasInnerPolygon
    {
        get { return innerPolygon != null && innerPolygon.Length >= 3; }
    }

    public bool IsPointInside(Vector3 worldPoint)
    {
        if (!HasInnerPolygon)
        {
            return true;
        }

        bool inside = false;
        int pointCount = innerPolygon.Length;

        for (int i = 0, j = pointCount - 1; i < pointCount; j = i++)
        {
            Vector3 pointA = innerPolygon[i];
            Vector3 pointB = innerPolygon[j];

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

    public bool IsVehiclePoseInside(VehicleCollisionShape collisionShape, Vector3 position, Quaternion rotation)
    {
        if (!HasInnerPolygon || collisionShape == null)
        {
            return true;
        }

        Vector3[] footprint = collisionShape.GetWorldFootprint(position, rotation, 0f);
        foreach (Vector3 corner in footprint)
        {
            if (!IsPointInside(corner))
            {
                return false;
            }
        }

        return true;
    }

    public void BuildRectangleFromSlot(ParkingSlot slot)
    {
        if (slot == null)
        {
            return;
        }

        slotId = slot.slotId;
        Transform basis = slot.GetParkingPoint();
        Vector3 center = basis.position;
        Vector3 right = basis.right;
        Vector3 forward = basis.forward;
        float halfWidth = slot.slotWidth * 0.5f;
        float halfDepth = slot.slotDepth * 0.5f;
        float margin = Mathf.Max(0f, allowedVehicleMargin);

        corners = new[]
        {
            center + right * -halfWidth + forward * -halfDepth,
            center + right * halfWidth + forward * -halfDepth,
            center + right * halfWidth + forward * halfDepth,
            center + right * -halfWidth + forward * halfDepth
        };

        innerPolygon = new[]
        {
            center + right * (-halfWidth + margin) + forward * (-halfDepth + margin),
            center + right * (halfWidth - margin) + forward * (-halfDepth + margin),
            center + right * (halfWidth - margin) + forward * (halfDepth - margin),
            center + right * (-halfWidth + margin) + forward * (halfDepth - margin)
        };
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebug)
        {
            return;
        }

        DrawPolygon(innerPolygon, polygonColor);
        DrawPolygon(corners, Color.gray);
    }

    private void DrawPolygon(Vector3[] polygon, Color color)
    {
        if (polygon == null || polygon.Length < 2)
        {
            return;
        }

        Gizmos.color = color;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector3 from = polygon[i];
            Vector3 to = polygon[(i + 1) % polygon.Length];
            Gizmos.DrawLine(from, to);
        }
    }
}
