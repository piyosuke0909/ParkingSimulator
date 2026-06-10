using UnityEngine;

public class ParkingSlotOccupancySensor : MonoBehaviour
{
    public ParkingSlot parkingSlot;

    [Header("Detection Settings")]
    public float requiredStayTime = 2.0f;

    private Car detectedCar;
    private float stayTimer;

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
    }
}