using UnityEngine;

[RequireComponent(typeof(VehicleCollisionShape))]
public class CarObstacleDetection : MonoBehaviour
{
    public float detectionDistance = 8f;
    public float warningDistance = 5f;
    public float emergencyStopDistance = 1.2f;
    public float sensorHeight = 0.65f;
    public float sensorWidth = 0.65f;
    public float frontSensorZ = 1.9f;
    public float rearSensorZ = -1.9f;
    public LayerMask obstacleMask = 0;
    public bool drawDebugRays = true;
    public float minimumSpeedFactor = 0.18f;

    public bool HasForwardObstacle { get; private set; }
    public float ForwardObstacleDistance { get; private set; }
    public RaycastHit LastForwardHit { get; private set; }

    private VehicleCollisionShape collisionShape;

    private void Awake()
    {
        collisionShape = GetComponent<VehicleCollisionShape>();
        SyncCollisionShape();
    }

    private void Update()
    {
        SyncCollisionShape();
        HasForwardObstacle = collisionShape.TryGetForwardObstacle(out RaycastHit hit);
        LastForwardHit = hit;
        ForwardObstacleDistance = HasForwardObstacle ? hit.distance : float.PositiveInfinity;
    }

    public bool ShouldSlowDown()
    {
        return HasForwardObstacle && ForwardObstacleDistance <= warningDistance;
    }

    public bool ShouldEmergencyStop()
    {
        return HasForwardObstacle && ForwardObstacleDistance <= emergencyStopDistance;
    }

    public float GetSpeedFactor()
    {
        if (!ShouldSlowDown())
        {
            return 1f;
        }

        float t = Mathf.InverseLerp(emergencyStopDistance, warningDistance, ForwardObstacleDistance);
        return Mathf.Lerp(minimumSpeedFactor, 1f, t);
    }

    private void SyncCollisionShape()
    {
        if (collisionShape == null)
        {
            collisionShape = GetComponent<VehicleCollisionShape>();
        }

        if (collisionShape == null)
        {
            return;
        }

        collisionShape.lookAheadDistance = detectionDistance;
        if (obstacleMask.value != 0)
        {
            collisionShape.obstacleLayerMask = obstacleMask;
        }
    }

    private void OnValidate()
    {
        detectionDistance = Mathf.Max(0f, detectionDistance);
        warningDistance = Mathf.Max(0f, warningDistance);
        emergencyStopDistance = Mathf.Max(0f, emergencyStopDistance);
        sensorHeight = Mathf.Max(0f, sensorHeight);
        sensorWidth = Mathf.Max(0f, sensorWidth);
        minimumSpeedFactor = Mathf.Clamp01(minimumSpeedFactor);
    }
}
