using System;
using UnityEngine;

public enum ParkingActionState
{
    Idle,
    Parking,
    Parked
}

public class ParkingAction : MonoBehaviour
{
    [Header("Movement")]
    public float parkingSpeed = 2f;
    public float rotationSpeed = 6f;
    public float stoppingDistance = 0.05f;
    public bool snapToParkingPointOnComplete = true;

    public event Action<ParkingSlot> ParkingCompleted;

    private ParkingSlot targetSlot;
    private ParkingActionState state = ParkingActionState.Idle;

    public ParkingActionState State
    {
        get { return state; }
    }

    public void StartParking(ParkingSlot slot)
    {
        if (slot == null)
        {
            Debug.LogWarning($"{name}: Parking slot is not set.");
            return;
        }

        targetSlot = slot;
        state = ParkingActionState.Parking;
    }

    private void Update()
    {
        if (state != ParkingActionState.Parking || targetSlot == null)
        {
            return;
        }

        Transform parkingPoint = targetSlot.GetParkingPoint();
        Vector3 targetPosition = parkingPoint.position;
        Vector3 toTarget = targetPosition - transform.position;
        toTarget.y = 0f;

        if (toTarget.magnitude > stoppingDistance)
        {
            if (toTarget.sqrMagnitude > 0.001f)
            {
                Quaternion moveRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, moveRotation, rotationSpeed * Time.deltaTime);
            }

            transform.position = Vector3.MoveTowards(transform.position, targetPosition, parkingSpeed * Time.deltaTime);
            return;
        }

        transform.rotation = Quaternion.Slerp(transform.rotation, parkingPoint.rotation, rotationSpeed * Time.deltaTime);
        if (Quaternion.Angle(transform.rotation, parkingPoint.rotation) > 2f)
        {
            return;
        }

        CompleteParking(parkingPoint);
    }

    private void CompleteParking(Transform parkingPoint)
    {
        if (snapToParkingPointOnComplete)
        {
            transform.position = parkingPoint.position;
            transform.rotation = parkingPoint.rotation;
        }

        state = ParkingActionState.Parked;
        targetSlot.SetOccupied();
        ParkingCompleted?.Invoke(targetSlot);
    }
}
