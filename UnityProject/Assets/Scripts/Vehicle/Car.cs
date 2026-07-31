using UnityEngine;

public class Car : MonoBehaviour
{
    [Header("Car Info")]
    public string carId = "Car_001";

    [Header("Current Parking Info")]
    public ParkingSlot currentParkingSlot;
    public bool isParked;

    [Header("Movement Info")]
    public Rigidbody carRigidbody;

    [Tooltip("この速度以下なら停止中とみなします。")]
    public float stoppedSpeedThreshold = 0.2f;

    private Vector3 previousPosition;
    private Vector3 transformVelocity;
    private bool hasTransformMotionSample;

    private void Awake()
    {
        FindReferences();
        ResetTransformMotionSample();
    }

    private void OnEnable()
    {
        ResetTransformMotionSample();
    }

    private void LateUpdate()
    {
        UpdateTransformMotionSample();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        FindReferences();
    }
#endif

    private void FindReferences()
    {
        if (carRigidbody == null)
        {
            carRigidbody = GetComponent<Rigidbody>();
        }
    }

    public Vector3 GetCurrentVelocity()
    {
        Vector3 rigidbodyVelocity = GetRigidbodyVelocity();

        // NPC_CarControllerはTransformを直接移動するため、Kinematic Rigidbodyの
        // velocityは常に0になることがあります。実際の移動量が大きい方を採用します。
        return transformVelocity.sqrMagnitude > rigidbodyVelocity.sqrMagnitude
            ? transformVelocity
            : rigidbodyVelocity;
    }

    public float GetCurrentSpeedMetersPerSecond()
    {
        return GetCurrentVelocity().magnitude;
    }

    public bool IsStopped()
    {
        return GetCurrentSpeedMetersPerSecond() <= Mathf.Max(0f, stoppedSpeedThreshold);
    }

    private void ResetTransformMotionSample()
    {
        previousPosition = transform.position;
        transformVelocity = Vector3.zero;
        hasTransformMotionSample = false;
    }

    private void UpdateTransformMotionSample()
    {
        Vector3 currentPosition = transform.position;
        float deltaTime = Time.deltaTime;

        if (!hasTransformMotionSample ||
            deltaTime <= Mathf.Epsilon ||
            float.IsNaN(deltaTime) ||
            float.IsInfinity(deltaTime))
        {
            transformVelocity = Vector3.zero;
            previousPosition = currentPosition;
            hasTransformMotionSample = true;
            return;
        }

        Vector3 measuredVelocity = (currentPosition - previousPosition) / deltaTime;

        if (!IsFinite(measuredVelocity))
        {
            measuredVelocity = Vector3.zero;
        }

        transformVelocity = measuredVelocity;
        previousPosition = currentPosition;
    }

    private Vector3 GetRigidbodyVelocity()
    {
        if (carRigidbody == null)
        {
            return Vector3.zero;
        }

#if UNITY_6000_0_OR_NEWER
        Vector3 velocity = carRigidbody.linearVelocity;
#else
        Vector3 velocity = carRigidbody.velocity;
#endif

        return IsFinite(velocity) ? velocity : Vector3.zero;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) &&
               !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) &&
               !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) &&
               !float.IsInfinity(value.z);
    }

    public void SetParked(ParkingSlot parkingSlot)
    {
        currentParkingSlot = parkingSlot;
        isParked = parkingSlot != null;

        if (parkingSlot != null)
        {
            Debug.Log($"{carId} は {parkingSlot.slotId} に駐車しました。");
        }
    }

    public void ClearParked()
    {
        if (currentParkingSlot != null)
        {
            Debug.Log($"{carId} は {currentParkingSlot.slotId} から出ました。");
        }

        currentParkingSlot = null;
        isParked = false;
    }

    public string GetCurrentSlotId()
    {
        if (currentParkingSlot == null)
        {
            return "";
        }

        return currentParkingSlot.slotId;
    }
}