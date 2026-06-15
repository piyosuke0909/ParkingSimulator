using System.Collections.Generic;
using UnityEngine;

public enum NPC_CarMoveState
{
    Idle,
    DrivingRoute,
    Parking,
    Parked,
    BackingOut,
    TurningAfterBackOut,
    Leaving,
    Finished
}

public class NPC_CarController : MonoBehaviour
{
    [Header("Route To Parking")]
    public List<Waypoint> route = new List<Waypoint>();
    public int currentWaypointIndex;

    [Header("Route To Exit")]
    public List<Waypoint> exitRoute = new List<Waypoint>();
    private int currentExitWaypointIndex;

    [Header("Parking Target")]
    public ParkingSlot targetParkingSlot;

    [Header("Movement")]
    public float moveSpeed = 20f;
    public float parkingSpeed = 3.5f;
    public float reverseSpeed = 2.0f;
    public float rotateSpeed = 10f;
    public float arriveDistance = 0.8f;
    public float parkingArriveDistance = 0.15f;
    public float backOutArriveDistance = 0.25f;

    [Header("Turn After Back Out")]
    public float turnCompleteAngle = 3f;

    [Header("Parking Wait")]
    public float parkWaitTime = 5f;
    private float parkTimer;

    [Header("State")]
    public NPC_CarMoveState moveState = NPC_CarMoveState.Idle;

    [Header("Finish Settings")]
    public bool destroyOnFinished = false;

    private Car car;

    private void Awake()
    {
        car = GetComponent<Car>();
    }

    private void Update()
    {
        switch (moveState)
        {
            case NPC_CarMoveState.Idle:
                break;

            case NPC_CarMoveState.DrivingRoute:
                FollowRoute();
                break;

            case NPC_CarMoveState.Parking:
                MoveToParkingPoint();
                break;

            case NPC_CarMoveState.Parked:
                WaitInParkingSlot();
                break;

            case NPC_CarMoveState.BackingOut:
                BackOutFromParkingSlot();
                break;

            case NPC_CarMoveState.TurningAfterBackOut:
                TurnAfterBackOut();
                break;

            case NPC_CarMoveState.Leaving:
                FollowExitRoute();
                break;

            case NPC_CarMoveState.Finished:
                break;
        }
    }

    public void SetRoute(List<Waypoint> newRoute)
    {
        route = newRoute;
        currentWaypointIndex = 0;

        if (route != null && route.Count > 0)
        {
            moveState = NPC_CarMoveState.DrivingRoute;
        }
        else
        {
            moveState = NPC_CarMoveState.Idle;
        }
    }

    public void SetRouteToParkingSlot(List<Waypoint> newRoute, ParkingSlot parkingSlot)
    {
        targetParkingSlot = parkingSlot;
        SetRoute(newRoute);
    }

    private void FollowRoute()
    {
        if (route == null || route.Count == 0)
        {
            moveState = NPC_CarMoveState.Idle;
            return;
        }

        if (currentWaypointIndex >= route.Count)
        {
            StartParking();
            return;
        }

        Waypoint targetWaypoint = route[currentWaypointIndex];

        if (targetWaypoint == null)
        {
            currentWaypointIndex++;
            return;
        }

        Vector3 targetPosition = targetWaypoint.transform.position;
        targetPosition.y = transform.position.y;

        MoveToTarget(targetPosition, moveSpeed);
        RotateToTarget(targetPosition);

        float distance = Vector3.Distance(transform.position, targetPosition);

        if (distance <= arriveDistance)
        {
            currentWaypointIndex++;
        }
    }

    private void StartParking()
    {
        if (targetParkingSlot == null || targetParkingSlot.parkingPoint == null)
        {
            moveState = NPC_CarMoveState.Idle;
            return;
        }

        moveState = NPC_CarMoveState.Parking;
    }

    private void MoveToParkingPoint()
    {
        if (targetParkingSlot == null || targetParkingSlot.parkingPoint == null)
        {
            moveState = NPC_CarMoveState.Idle;
            return;
        }

        Vector3 targetPosition = targetParkingSlot.parkingPoint.position;
        targetPosition.y = transform.position.y;

        MoveToTarget(targetPosition, parkingSpeed);
        RotateToTarget(targetPosition);

        float distance = Vector3.Distance(transform.position, targetPosition);

        if (distance <= parkingArriveDistance)
        {
            CompleteParking();
        }
    }

    private void CompleteParking()
    {
        if (targetParkingSlot != null)
        {
            transform.position = new Vector3(
                targetParkingSlot.parkingPoint.position.x,
                transform.position.y,
                targetParkingSlot.parkingPoint.position.z
            );

            transform.rotation = targetParkingSlot.parkingPoint.rotation;

            targetParkingSlot.SetOccupied();

            if (car != null)
            {
                car.SetParked(targetParkingSlot);
            }
        }

        parkTimer = 0f;
        moveState = NPC_CarMoveState.Parked;
    }

    private void WaitInParkingSlot()
    {
        parkTimer += Time.deltaTime;

        if (parkTimer >= parkWaitTime)
        {
            StartLeaving();
        }
    }

    private void StartLeaving()
    {
        if (targetParkingSlot != null)
        {
            targetParkingSlot.SetEmpty();
        }

        if (car != null)
        {
            car.ClearParked();
        }

        currentExitWaypointIndex = 0;

        if (exitRoute != null && exitRoute.Count > 0)
        {
            moveState = NPC_CarMoveState.BackingOut;
        }
        else
        {
            moveState = NPC_CarMoveState.Finished;
        }
    }

    private void BackOutFromParkingSlot()
    {
        if (targetParkingSlot == null || targetParkingSlot.accessWaypoint == null)
        {
            FinishDriving();
            return;
        }

        Waypoint backOutWaypoint = targetParkingSlot.accessWaypoint;

        Vector3 targetPosition = backOutWaypoint.transform.position;
        targetPosition.y = transform.position.y;

        // ’“ŽÔ’†‚ÌŒü‚«‚ð•Û‚Á‚½‚Ü‚ÜƒoƒbƒN‚·‚é
        MoveToTarget(targetPosition, reverseSpeed);

        float distance = Vector3.Distance(transform.position, targetPosition);

        if (distance <= backOutArriveDistance)
        {
            transform.position = new Vector3(
                targetPosition.x,
                transform.position.y,
                targetPosition.z
            );

            currentExitWaypointIndex = 0;

            if (exitRoute == null || exitRoute.Count == 0)
            {
                FinishDriving();
            }
            else
            {
                moveState = NPC_CarMoveState.TurningAfterBackOut;
            }
        }
    }

    private void SkipReachedExitWaypoints()
    {
        while (currentExitWaypointIndex < exitRoute.Count)
        {
            Waypoint waypoint = exitRoute[currentExitWaypointIndex];

            if (waypoint == null)
            {
                currentExitWaypointIndex++;
                continue;
            }

            Vector3 waypointPosition = waypoint.transform.position;
            waypointPosition.y = transform.position.y;

            float distance = Vector3.Distance(transform.position, waypointPosition);

            if (distance > arriveDistance)
            {
                break;
            }

            currentExitWaypointIndex++;
        }
    }


    private void TurnAfterBackOut()
    {
        if (exitRoute == null || exitRoute.Count == 0)
        {
            FinishDriving();
            return;
        }

        SkipReachedExitWaypoints();

        if (currentExitWaypointIndex >= exitRoute.Count)
        {
            FinishDriving();
            return;
        }

        Waypoint nextWaypoint = exitRoute[currentExitWaypointIndex];

        if (nextWaypoint == null)
        {
            currentExitWaypointIndex++;
            return;
        }

        Vector3 targetPosition = nextWaypoint.transform.position;
        targetPosition.y = transform.position.y;

        Vector3 direction = targetPosition - transform.position;

        if (direction.sqrMagnitude <= 0.001f)
        {
            currentExitWaypointIndex++;
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotateSpeed * Time.deltaTime
        );

        float angle = Quaternion.Angle(transform.rotation, targetRotation);

        if (angle <= turnCompleteAngle)
        {
            transform.rotation = targetRotation;
            moveState = NPC_CarMoveState.Leaving;
        }
    }

    private void FollowExitRoute()
    {
        if (exitRoute == null || exitRoute.Count == 0)
        {
            FinishDriving();
            return;
        }

        if (currentExitWaypointIndex >= exitRoute.Count)
        {
            FinishDriving();
            return;
        }

        Waypoint targetWaypoint = exitRoute[currentExitWaypointIndex];

        if (targetWaypoint == null)
        {
            currentExitWaypointIndex++;
            return;
        }

        Vector3 targetPosition = targetWaypoint.transform.position;
        targetPosition.y = transform.position.y;

        MoveToTarget(targetPosition, moveSpeed);
        RotateToTarget(targetPosition);

        float distance = Vector3.Distance(transform.position, targetPosition);

        if (distance <= arriveDistance)
        {
            currentExitWaypointIndex++;
        }
    }

    private void FinishDriving()
    {
        moveState = NPC_CarMoveState.Finished;

        if (destroyOnFinished)
        {
            Destroy(gameObject);
        }
    }

    private void MoveToTarget(Vector3 targetPosition, float speed)
    {
        transform.position = Vector3.MoveTowards(
            transform.position,
            targetPosition,
            speed * Time.deltaTime
        );
    }

    private void RotateToTarget(Vector3 targetPosition)
    {
        Vector3 direction = targetPosition - transform.position;

        if (direction.sqrMagnitude <= 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotateSpeed * Time.deltaTime
        );
    }
}