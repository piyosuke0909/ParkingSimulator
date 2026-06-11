using UnityEngine;

public class VehicleCollisionShape : MonoBehaviour
{
    [Header("Body")]
    public string vehicleType = "Standard";
    public float bodyLength = 4.5f;
    public float bodyWidth = 1.8f;
    public float bodyHeight = 1.6f;
    public float frontOverhang = 0.8f;
    public float rearOverhang = 0.8f;

    [Header("Safety Margin")]
    public float sideSafetyMargin = 0.2f;
    public float frontSafetyMargin = 0.6f;
    public float rearSafetyMargin = 0.2f;

    [Header("Forward Detection")]
    public LayerMask obstacleLayerMask = 0;
    public float lookAheadDistance = 4f;
    public bool drawDebug = true;

    public Vector3 GetHalfExtents(float extraMargin = 0f)
    {
        float halfWidth = bodyWidth * 0.5f + sideSafetyMargin + extraMargin;
        float halfHeight = bodyHeight * 0.5f;
        float halfLength = bodyLength * 0.5f + Mathf.Max(frontSafetyMargin, rearSafetyMargin) + extraMargin;
        return new Vector3(halfWidth, halfHeight, halfLength);
    }

    public Vector3 GetCastCenter(Vector3 position, Quaternion rotation)
    {
        float offset = (frontSafetyMargin - rearSafetyMargin) * 0.5f;
        return position + rotation * new Vector3(0f, bodyHeight * 0.5f, offset);
    }

    public Vector3[] GetWorldFootprint(Vector3 position, Quaternion rotation, float extraMargin)
    {
        float halfWidth = bodyWidth * 0.5f + extraMargin;
        float front = bodyLength * 0.5f + extraMargin;
        float rear = bodyLength * 0.5f + extraMargin;

        return new[]
        {
            position + rotation * new Vector3(-halfWidth, 0f, front),
            position + rotation * new Vector3(halfWidth, 0f, front),
            position + rotation * new Vector3(halfWidth, 0f, -rear),
            position + rotation * new Vector3(-halfWidth, 0f, -rear)
        };
    }

    public bool TryGetForwardObstacle(out RaycastHit hit)
    {
        hit = default(RaycastHit);
        Vector3 halfExtents = GetHalfExtents();
        Vector3 center = GetCastCenter(transform.position, transform.rotation);
        RaycastHit[] hits = Physics.BoxCastAll(
            center,
            halfExtents,
            transform.forward,
            transform.rotation,
            lookAheadDistance,
            obstacleLayerMask,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.MaxValue;
        bool found = false;

        foreach (RaycastHit candidate in hits)
        {
            if (candidate.collider == null)
            {
                continue;
            }

            if (candidate.collider.transform.root == transform.root)
            {
                continue;
            }

            if (candidate.distance < nearestDistance)
            {
                nearestDistance = candidate.distance;
                hit = candidate;
                found = true;
            }
        }

        return found;
    }

    private void OnValidate()
    {
        bodyLength = Mathf.Max(0.01f, bodyLength);
        bodyWidth = Mathf.Max(0.01f, bodyWidth);
        bodyHeight = Mathf.Max(0.01f, bodyHeight);
        sideSafetyMargin = Mathf.Max(0f, sideSafetyMargin);
        frontSafetyMargin = Mathf.Max(0f, frontSafetyMargin);
        rearSafetyMargin = Mathf.Max(0f, rearSafetyMargin);
        lookAheadDistance = Mathf.Max(0f, lookAheadDistance);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebug)
        {
            return;
        }

        Gizmos.color = Color.yellow;
        Vector3 center = GetCastCenter(transform.position, transform.rotation);
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.forward * (lookAheadDistance * 0.5f), GetHalfExtents() * 2f + new Vector3(0f, 0f, lookAheadDistance));
        Gizmos.matrix = previousMatrix;
    }
}
