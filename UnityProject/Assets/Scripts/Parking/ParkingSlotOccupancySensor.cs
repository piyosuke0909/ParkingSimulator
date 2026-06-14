using UnityEngine;

public class ParkingSlotOccupancySensor : MonoBehaviour
{
    public ParkingSlot parkingSlot;

    [Header("Detection Settings")]
    public float requiredStayTime = 1.5f;
    public float requiredLeaveTime = 2.0f;

    private Car detectedCar;
    private float stayTimer;
    private float leaveTimer;
    private bool carInside;

    private void Reset()
    {
        parkingSlot = GetComponentInParent<ParkingSlot>();
    }

    private void Awake()
    {
        if (parkingSlot == null)
        {
            parkingSlot = GetComponentInParent<ParkingSlot>();
        }
    }

    private void Update()
    {
        if (parkingSlot == null)
        {
            return;
        }

        if (detectedCar == null)
        {
            stayTimer = 0f;
            return;
        }

        stayTimer += Time.deltaTime;

        if (stayTimer >= requiredStayTime)
        {
            parkingSlot.SetOccupied();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        Car car = other.GetComponentInParent<Car>();

        if (car == null)
        {
            return;
        }

        detectedCar = car;
        stayTimer = 0f;

        Debug.Log($"{parkingSlot.slotId} Ç… {car.carId} Ç™ì¸ÇËÇ‹ÇµÇΩÅB");
    }

    private void OnTriggerExit(Collider other)
    {
        Car car = other.GetComponentInParent<Car>();

        if (car == null)
        {
            return;
        }

        if (car == detectedCar)
        {
            Debug.Log($"{parkingSlot.slotId} Ç©ÇÁ {car.carId} Ç™èoÇ‹ÇµÇΩÅB");

            detectedCar = null;
            stayTimer = 0f;

            parkingSlot.SetEmpty();
        }

        if (carInside)
        {
            stayTimer += Time.deltaTime;
            leaveTimer = 0f;

            if (stayTimer >= requiredStayTime)
            {
                parkingSlot.SetOccupied();
            }
        }
        else
        {
            leaveTimer += Time.deltaTime;
            stayTimer = 0f;

            if (leaveTimer >= requiredLeaveTime)
            {
                parkingSlot.SetEmpty();
            }
        }

    }
}