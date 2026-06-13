using System;
using UnityEngine;

public enum ParkingActionState
{
    Idle,
    Parking,
    Parked,
    Failed
}

public class ParkingAction : MonoBehaviour
{
    [Header("Movement")]
    public float parkingSpeed = 2f;
    public float rotationSpeed = 6f;
    public float stoppingDistance = 0.05f;
    public bool snapToParkingPointOnComplete = true;
    public bool stopForObstaclesDuringParking = false;

    public event Action<ParkingSlot> ParkingCompleted;
    public event Action<ParkingSlot, string> ParkingFailed;

    private ParkingSlot targetSlot;
    private ManeuverPath maneuverPath;
    private ParkingManeuverType maneuverType = ParkingManeuverType.FrontIn;
    private readonly System.Collections.Generic.List<Vector3> parkingPath = new System.Collections.Generic.List<Vector3>();
    private int currentPathIndex;
    private ParkingActionState state = ParkingActionState.Idle;
    private VehicleCollisionShape collisionShape;
    private Quaternion finalParkingRotation;

    public ParkingActionState State
    {
        get { return state; }
    }

    public void StartParking(ParkingSlot slot)
    {
        StartParking(slot, ParkingManeuverType.FrontIn, null);
    }

    public void StartParking(ParkingSlot slot, ParkingManeuverType selectedManeuverType, ManeuverPath selectedManeuverPath)
    {
        if (slot == null)
        {
            Debug.LogWarning($"{name}: Parking slot is not set.");
            return;
        }

        targetSlot = slot;
        maneuverType = selectedManeuverType;
        maneuverPath = selectedManeuverPath;
        finalParkingRotation = slot.GetParkingPoint().rotation;
        BuildParkingPath();
        state = ParkingActionState.Parking;
    }

    private void Update()
    {
        if (state != ParkingActionState.Parking || targetSlot == null)
        {
            return;
        }

        Transform parkingPoint = targetSlot.GetParkingPoint();

        if (currentPathIndex >= parkingPath.Count)
        {
            RotateAndComplete(parkingPoint);
            return;
        }

        Vector3 targetPosition = parkingPath[currentPathIndex];
        Vector3 toTarget = targetPosition - transform.position;
        toTarget.y = 0f;

        if (toTarget.magnitude > stoppingDistance)
        {
            if (IsForwardBlocked())
            {
                return;
            }

            if (toTarget.sqrMagnitude > 0.001f)
            {
                Quaternion moveRotation = GetTargetRotation(toTarget.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, moveRotation, rotationSpeed * Time.deltaTime);
            }

            transform.position = Vector3.MoveTowards(transform.position, targetPosition, parkingSpeed * Time.deltaTime);
            return;
        }

        currentPathIndex++;
    }

    private void RotateAndComplete(Transform parkingPoint)
    {
        transform.rotation = Quaternion.Slerp(transform.rotation, finalParkingRotation, rotationSpeed * Time.deltaTime);
        if (Quaternion.Angle(transform.rotation, finalParkingRotation) > 2f)
        {
            return;
        }

        CompleteParking(parkingPoint);
    }

    private void BuildParkingPath()
    {
        parkingPath.Clear();
        currentPathIndex = 0;

        if (maneuverPath != null)
        {
            parkingPath.AddRange(maneuverPath.BuildWorldPoints(targetSlot));
        }

        if (parkingPath.Count == 0)
        {
            Transform entryPoint = maneuverType == ParkingManeuverType.ReverseIn ? targetSlot.reverseEntryPoint : targetSlot.frontEntryPoint;

            if (entryPoint != null)
            {
                parkingPath.Add(entryPoint.position);
            }

            parkingPath.Add(targetSlot.GetParkingPoint().position);
        }

        RemoveAlreadyReachedFirstPoint();
    }

    private void RemoveAlreadyReachedFirstPoint()
    {
        if (parkingPath.Count == 0)
        {
            return;
        }

        float skipDistance = Mathf.Max(stoppingDistance, 0.35f);
        Vector3 toFirstPoint = parkingPath[0] - transform.position;
        toFirstPoint.y = 0f;
        if (toFirstPoint.magnitude <= skipDistance)
        {
            parkingPath.RemoveAt(0);
        }
    }

    private Quaternion GetTargetRotation(Vector3 movementDirection)
    {
        bool isFinalSegment = currentPathIndex >= parkingPath.Count - 1;
        if (isFinalSegment)
        {
            return finalParkingRotation;
        }

        if (movementDirection.sqrMagnitude <= 0.001f)
        {
            return transform.rotation;
        }

        return Quaternion.LookRotation(movementDirection, Vector3.up);
    }

    private bool IsForwardBlocked()
    {
        if (!stopForObstaclesDuringParking)
        {
            return false;
        }

        if (collisionShape == null)
        {
            collisionShape = GetComponent<VehicleCollisionShape>();
        }

        if (collisionShape == null)
        {
            return false;
        }

        RaycastHit hit;
        return collisionShape.TryGetForwardObstacle(out hit);
    }

    private void CompleteParking(Transform parkingPoint)
    {
        if (!IsFinalPoseSafe(parkingPoint))
        {
            FailParking("Final parking pose is outside the slot geometry or overlaps an obstacle.");
            return;
        }

        if (snapToParkingPointOnComplete)
        {
            transform.position = parkingPoint.position;
            transform.rotation = finalParkingRotation;
        }

        state = ParkingActionState.Parked;
        targetSlot.SetOccupied();
        ParkingCompleted?.Invoke(targetSlot);
    }

    private bool IsFinalPoseSafe(Transform parkingPoint)
    {
        if (collisionShape == null)
        {
            collisionShape = GetComponent<VehicleCollisionShape>();
        }

        ParkingSlotGeometry geometry = targetSlot.GetGeometry();
        if (geometry != null && collisionShape != null && !geometry.IsVehiclePoseInside(collisionShape, parkingPoint.position, parkingPoint.rotation))
        {
            return false;
        }

        if (collisionShape != null && HasBlockingOverlap(parkingPoint.position, parkingPoint.rotation))
        {
            return false;
        }

        return true;
    }

    private bool HasBlockingOverlap(Vector3 position, Quaternion rotation)
    {
        Vector3 center = collisionShape.GetCastCenter(position, rotation);
        Vector3 halfExtents = collisionShape.GetHalfExtents();
        Collider[] colliders = Physics.OverlapBox(center, halfExtents, rotation, collisionShape.obstacleLayerMask, QueryTriggerInteraction.Ignore);

        foreach (Collider collider in colliders)
        {
            if (collider != null && collider.transform.root != transform.root)
            {
                return true;
            }
        }

        return false;
    }

    private void FailParking(string reason)
    {
        state = ParkingActionState.Failed;
        ParkingFailed?.Invoke(targetSlot, reason);
    }
}
