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
    public float moveSpeed = 8f;
    public float parkingSpeed = 2.5f;
    public float reverseSpeed = 2.0f;
    public float rotateSpeed = 5f;
    public float arriveDistance = 0.8f;
    public float parkingArriveDistance = 0.15f;
    public float backOutArriveDistance = 0.25f;

    [Header("Turn After Back Out")]
    public float turnCompleteAngle = 3f;

    [Header("Parking Wait")]
    public float parkWaitTime = 5f;
    private float parkTimer;

    [Header("Traffic Zone")]
    public bool useTrafficZone = true;
    public TrafficZone currentTrafficZone;
    public TrafficZone waitingTrafficZone;
    public bool isPriorityTrafficZoneEntry;
    public bool isWaitingForTrafficZone;
    public bool logTrafficZoneDebug = false;

    [Header("Front Vehicle Detection")]
    public bool useFrontVehicleDetection = true;
    public Transform frontSensor;
    public LayerMask carLayerMask;
    public float frontCheckDistance = 15f;
    public float frontCheckRadius = 1.8f;
    public float frontStopHoldTime = 0.5f;
    public bool drawFrontCheckDebug = true;
    public bool logFrontVehicleDetection = false;

    [Header("Stop State")]
    public bool isStoppedByFrontCar;
    private float frontStopTimer;

    [Header("State")]
    public NPC_CarMoveState moveState = NPC_CarMoveState.Idle;

    [Header("Finish Settings")]
    public bool destroyOnFinished = false;

    private Car car;
    private Rigidbody carRigidbody;

    private void Awake()
    {
        car = GetComponent<Car>();
        carRigidbody = GetComponent<Rigidbody>();

        if (frontSensor == null)
        {
            Transform sensor = transform.Find("FrontSensor");

            if (sensor != null)
            {
                frontSensor = sensor;
            }
            else
            {
                Debug.LogWarning($"{name}: FrontSensor が見つかりません。NPC_Car直下に FrontSensor を作成してください。");
            }
        }

        ResetRuntimeState();
    }

    private void OnDestroy()
    {
        ReleaseCurrentTrafficZone();
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

    private void ResetRuntimeState()
    {
        isStoppedByFrontCar = false;
        isWaitingForTrafficZone = false;

        frontStopTimer = 0f;
        parkTimer = 0f;

        currentTrafficZone = null;

        if (moveState == NPC_CarMoveState.Finished)
        {
            moveState = NPC_CarMoveState.Idle;
        }
    }

    public void SetRoute(List<Waypoint> newRoute)
    {
        ReleaseCurrentTrafficZone();

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
            ReleaseCurrentTrafficZone();
            moveState = NPC_CarMoveState.Idle;
            return;
        }

        if (currentWaypointIndex >= route.Count)
        {
            ReleaseCurrentTrafficZone();
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

        if (!CanEnterTrafficZone(targetWaypoint))
        {
            StopCarCompletely();
            return;
        }

        if (ShouldHoldStopForFrontCar())
        {
            StopCarCompletely();
            return;
        }

        MoveToTarget(targetPosition, moveSpeed);
        RotateToTarget(targetPosition);

        float distance = Vector3.Distance(transform.position, targetPosition);

        if (distance <= arriveDistance)
        {
            TryReleaseTrafficZoneAtWaypoint(targetWaypoint);
            currentWaypointIndex++;
        }
    }

    private void StartParking()
    {
        ReleaseCurrentTrafficZone();

        if (targetParkingSlot == null || targetParkingSlot.parkingPoint == null)
        {
            moveState = NPC_CarMoveState.Idle;
            return;
        }

        StopCarCompletely();
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
        ReleaseCurrentTrafficZone();

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

        StopCarCompletely();

        parkTimer = 0f;
        moveState = NPC_CarMoveState.Parked;
    }

    private void WaitInParkingSlot()
    {
        StopCarCompletely();

        parkTimer += Time.deltaTime;

        if (parkTimer >= parkWaitTime)
        {
            StartLeaving();
        }
    }

    private void StartLeaving()
    {
        ReleaseCurrentTrafficZone();

        if (targetParkingSlot != null)
        {
            targetParkingSlot.SetEmpty();
        }

        if (car != null)
        {
            car.ClearParked();
        }

        currentExitWaypointIndex = 0;

        if (targetParkingSlot != null && targetParkingSlot.accessWaypoint != null)
        {
            moveState = NPC_CarMoveState.BackingOut;
        }
        else
        {
            FinishDriving();
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

        MoveToTarget(targetPosition, reverseSpeed);

        float distance = Vector3.Distance(transform.position, targetPosition);

        if (distance <= backOutArriveDistance)
        {
            transform.position = new Vector3(
                targetPosition.x,
                transform.position.y,
                targetPosition.z
            );

            StopCarCompletely();

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

        StopCarCompletely();

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
            ReleaseCurrentTrafficZone();
            FinishDriving();
            return;
        }

        if (currentExitWaypointIndex >= exitRoute.Count)
        {
            ReleaseCurrentTrafficZone();
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

        if (!CanEnterTrafficZone(targetWaypoint))
        {
            StopCarCompletely();
            return;
        }

        if (ShouldHoldStopForFrontCar())
        {
            StopCarCompletely();
            return;
        }

        MoveToTarget(targetPosition, moveSpeed);
        RotateToTarget(targetPosition);

        float distance = Vector3.Distance(transform.position, targetPosition);

        if (distance <= arriveDistance)
        {
            TryReleaseTrafficZoneAtWaypoint(targetWaypoint);
            currentExitWaypointIndex++;
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

    private bool CanEnterTrafficZone(Waypoint targetWaypoint)
    {
        if (!useTrafficZone)
        {
            return true;
        }

        if (targetWaypoint == null)
        {
            return true;
        }

        TrafficZone targetZone = targetWaypoint.trafficZoneToEnter;

        if (targetZone == null)
        {
            return true;
        }

        if (!targetWaypoint.waitBeforeTrafficZone)
        {
            return true;
        }

        if (currentTrafficZone == targetZone)
        {
            isWaitingForTrafficZone = false;
            waitingTrafficZone = null;
            return true;
        }

        if (currentTrafficZone != null && currentTrafficZone != targetZone)
        {
            ReleaseCurrentTrafficZone();
        }

        if (waitingTrafficZone != null && waitingTrafficZone != targetZone)
        {
            waitingTrafficZone.RemoveFromQueue(this);
            waitingTrafficZone = null;
        }

        bool canEnter = targetZone.TryEnter(
            this,
            targetWaypoint.isPriorityTrafficZoneEntry
        );

        if (canEnter)
        {
            currentTrafficZone = targetZone;
            waitingTrafficZone = null;
            isWaitingForTrafficZone = false;

            if (logTrafficZoneDebug)
            {
                Debug.Log($"{name}: TrafficZone 進入許可 {targetZone.zoneId}");
            }

            return true;
        }

        waitingTrafficZone = targetZone;
        isWaitingForTrafficZone = true;

        if (logTrafficZoneDebug)
        {
            Debug.Log($"{name}: TrafficZone 順番待ち {targetZone.zoneId}");
        }

        return false;
    }

    private void TryReleaseTrafficZoneAtWaypoint(Waypoint reachedWaypoint)
    {
        if (!useTrafficZone)
        {
            return;
        }

        if (reachedWaypoint == null)
        {
            return;
        }

        if (!reachedWaypoint.releaseTrafficZoneHere)
        {
            return;
        }

        ReleaseCurrentTrafficZone();
    }

    private void ReleaseCurrentTrafficZone()
    {
        if (waitingTrafficZone != null)
        {
            waitingTrafficZone.RemoveFromQueue(this);
            waitingTrafficZone = null;
        }

        if (currentTrafficZone == null)
        {
            isWaitingForTrafficZone = false;
            return;
        }

        TrafficZone releasedZone = currentTrafficZone;
        releasedZone.Exit(this);

        if (logTrafficZoneDebug)
        {
            Debug.Log($"{name}: TrafficZone 解放 {releasedZone.zoneId}");
        }

        currentTrafficZone = null;
        isWaitingForTrafficZone = false;
    }

    private bool ShouldHoldStopForFrontCar()
    {
        if (ShouldStopForFrontCar())
        {
            frontStopTimer = frontStopHoldTime;
            return true;
        }

        if (frontStopTimer > 0f)
        {
            frontStopTimer -= Time.deltaTime;
            isStoppedByFrontCar = true;
            return true;
        }

        isStoppedByFrontCar = false;
        return false;
    }

    private bool ShouldStopForFrontCar()
    {
        if (!useFrontVehicleDetection)
        {
            isStoppedByFrontCar = false;
            return false;
        }

        if (frontSensor == null)
        {
            isStoppedByFrontCar = false;
            return false;
        }

        Vector3 origin = frontSensor.position;
        Vector3 direction = frontSensor.forward;

        if (drawFrontCheckDebug)
        {
            Debug.DrawRay(origin, direction * frontCheckDistance, Color.red);
        }

        RaycastHit[] hits = Physics.SphereCastAll(
            origin,
            frontCheckRadius,
            direction,
            frontCheckDistance,
            carLayerMask
        );

        foreach (RaycastHit hit in hits)
        {
            Car hitCar = hit.collider.GetComponentInParent<Car>();

            if (hitCar == null)
            {
                continue;
            }

            if (hitCar == car)
            {
                continue;
            }

            NPC_CarController hitCarController = hitCar.GetComponent<NPC_CarController>();

            if (hitCarController != null)
            {
                if (ShouldIgnoreCarForFrontDetection(hitCarController))
                {
                    continue;
                }
            }

            if (logFrontVehicleDetection)
            {
                Debug.Log($"{name}: 前方車両を検知しました → {hitCar.carId}");
            }

            isStoppedByFrontCar = true;
            return true;
        }

        isStoppedByFrontCar = false;
        return false;
    }

    private bool ShouldIgnoreCarForFrontDetection(NPC_CarController otherCarController)
    {
        if (otherCarController == null)
        {
            return false;
        }

        // 駐車済み・終了済みの車はすでに別処理でも無視しているが、
        // 念のためここでも無視対象にする
        if (otherCarController.moveState == NPC_CarMoveState.Parked ||
            otherCarController.moveState == NPC_CarMoveState.Finished)
        {
            return true;
        }

        // 自分がTrafficZone内にいる間、
        // Zone外で順番待ちしている車は前方検知対象から外す
        if (currentTrafficZone != null)
        {
            bool otherIsWaitingOutsideThisZone =
                otherCarController.isWaitingForTrafficZone &&
                otherCarController.currentTrafficZone == null &&
                otherCarController.waitingTrafficZone == currentTrafficZone;

            if (otherIsWaitingOutsideThisZone)
            {
                return true;
            }
        }

        return false;
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

    private void StopCarCompletely()
    {
        if (carRigidbody == null)
        {
            return;
        }

        if (carRigidbody.isKinematic)
        {
            return;
        }

#if UNITY_6000_0_OR_NEWER
        carRigidbody.linearVelocity = Vector3.zero;
#else
        carRigidbody.velocity = Vector3.zero;
#endif

        carRigidbody.angularVelocity = Vector3.zero;
    }

    private void FinishDriving()
    {
        ReleaseCurrentTrafficZone();

        StopCarCompletely();

        moveState = NPC_CarMoveState.Finished;

        if (destroyOnFinished)
        {
            Destroy(gameObject);
        }
    }
}
