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

    private void Awake()
    {
        FindReferences();
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

    public bool IsStopped()
    {
        if (carRigidbody == null)
        {
            return true;
        }

#if UNITY_6000_0_OR_NEWER
        return carRigidbody.linearVelocity.magnitude <= stoppedSpeedThreshold;
#else
        return carRigidbody.velocity.magnitude <= stoppedSpeedThreshold;
#endif
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