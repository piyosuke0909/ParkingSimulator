using System.Collections.Generic;
using UnityEngine;

public enum NPC_CarMoveState
{
    Idle,
    DrivingRoute,
    Parking,
    Parked,
    WaitingToBackOut,
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

    [Header("Back Out Safety Check")]
    public bool useBackOutSafetyCheck = true;
    public LayerMask backOutCheckLayerMask;
    public Vector3 backOutCheckBoxSize = new Vector3(12f, 4f, 12f);
    public float backOutRetryInterval = 0.5f;
    public bool checkWhileBackingOut = false;
    public bool drawBackOutCheckGizmo = true;
    public bool logBackOutSafetyDebug = false;

    [Tooltip("バック確認時、すでに駐車中の車は無視します。")]
    public bool ignoreParkedCarsInBackOutCheck = true;

    [Tooltip("AccessWaypointを基準にした確認範囲の中心位置補正です。向かい側スロットを拾う場合は通路側へずらします。")]
    public Vector3 backOutCheckCenterOffset = Vector3.zero;

    private float backOutRetryTimer;

    [Header("Back Out Yield")]
    [Tooltip("バック中断時、検知した相手車両を通すために、自分を一時的に前方検知対象から外させます。")]
    public bool useBackOutYieldToPassingCar = true;

    [Tooltip("バック中断時に、相手車両へ譲っている状態を保持する秒数です。")]
    public float backOutYieldMemoryTime = 2.0f;

    private NPC_CarController yieldingToCar;
    private float backOutYieldTimer;

    [Header("Front Vehicle Detection")]
    public bool useFrontVehicleDetection = true;
    public Transform frontSensor;
    public LayerMask carLayerMask;
    public float frontCheckDistance = 15f;
    public float frontCheckRadius = 1.8f;
    public float frontStopHoldTime = 0.5f;
    public bool drawFrontCheckDebug = true;
    public bool logFrontVehicleDetection = false;

    [Header("Turn In Place At Waypoint")]
    [Tooltip("Waypointで大きく方向転換する場合、一度停止してからその場で回転します。")]
    public bool useTurnInPlaceAtWaypoint = true;

    [Tooltip("この角度以上向きがずれている場合、その場回転を行います。")]
    public float turnInPlaceStartAngle = 35f;

    [Tooltip("この角度以下になったら、その場回転完了とします。")]
    public float turnInPlaceCompleteAngle = 3f;

    [Tooltip("Waypoint到着後、回転を始める前に停止する時間です。")]
    public float turnInPlacePauseTime = 0.15f;

    [Tooltip("その場回転中のログを出します。")]
    public bool logTurnInPlaceDebug = false;

    private bool isTurningInPlaceAtWaypoint;
    private float turnInPlacePauseTimer;

    [Header("Temporary Front Ray Ignore")]
    [Tooltip("車同士が鉢合わせで停止したとき、片方だけ一時的に前方検知を無効化します。")]
    public bool useTemporaryFrontRayIgnore = true;

    [Tooltip("前方検知を一時的に無効化する秒数です。")]
    public float frontRayIgnoreDuration = 0.6f;

    [Tooltip("鉢合わせ判定に使う距離です。")]
    public float deadlockCheckDistance = 12f;

    [Tooltip("正面対向だけでなく、直角方向の鉢合わせも解消対象にします。")]
    public bool allowCrossDirectionDeadlockResolve = true;

    [Tooltip("鉢合わせ解消ログを出します。")]
    public bool logDeadlockResolve = true;

    private float frontRayIgnoreTimer;
    private NPC_CarController lastDetectedFrontCarController;

    [Header("Waypoint Reservation")]
    [Tooltip("ONにすると、次のWaypointと道を予約してから進みます。")]
    public bool useWaypointReservation = true;

    [Tooltip("Waypoint予約のデバッグログを出します。")]
    public bool logWaypointReservationDebug = false;

    [Tooltip("SetRoute時、車がroute[0]付近にいる場合、そのWaypointを現在地として占有します。")]
    public bool occupyFirstWaypointOnRouteStart = true;

    [Tooltip("route[0]を現在地として扱う最大距離です。")]
    public float firstWaypointOccupyDistance = 6f;

    private Waypoint reservedWaypoint;
    private Waypoint occupiedWaypoint;
    private RoadSection reservedRoadSection;
    private bool isStoppedByReservation;
    private Waypoint waitingWaypoint;
    private RoadSection waitingRoadSection;

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
    }

    private void OnDestroy()
    {
        EndTurnInPlaceAtWaypoint();
        ReleaseWaypointReservation();

        if (targetParkingSlot != null)
        {
            targetParkingSlot.TryReleaseAfterExit(this);
            targetParkingSlot = null;
        }
    }

    private void Update()
    {
        if (frontRayIgnoreTimer > 0f)
        {
            frontRayIgnoreTimer -= Time.deltaTime;
        }

        if (backOutYieldTimer > 0f)
        {
            backOutYieldTimer -= Time.deltaTime;

            if (backOutYieldTimer <= 0f)
            {
                yieldingToCar = null;
            }
        }

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

            case NPC_CarMoveState.WaitingToBackOut:
                WaitUntilCanBackOut();
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
        ReleaseWaypointReservation();

        route = newRoute;
        currentWaypointIndex = 0;

        if (route != null && route.Count > 0)
        {
            // route[0] が現在位置に近い場合だけ、現在占有中のWaypointとして登録する。
            // これにより、次の区間へ進むときに RoadSection を正しく取得できる。
            if (useWaypointReservation && occupyFirstWaypointOnRouteStart)
            {
                Waypoint firstWaypoint = route[0];

                if (firstWaypoint != null)
                {
                    Vector3 firstPosition = firstWaypoint.transform.position;
                    firstPosition.y = transform.position.y;

                    float distanceToFirst = Vector3.Distance(transform.position, firstPosition);

                    if (distanceToFirst <= firstWaypointOccupyDistance)
                    {
                        ArriveAtWaypoint(firstWaypoint, GetNextRouteWaypoint(0));
                        currentWaypointIndex = 1;
                    }
                }
            }

            moveState = NPC_CarMoveState.DrivingRoute;
        }
        else
        {
            moveState = NPC_CarMoveState.Idle;
        }
    }

    private Waypoint GetNextRouteWaypoint(int index)
    {
        if (route == null)
        {
            return null;
        }

        int nextIndex = index + 1;

        if (nextIndex < 0 || nextIndex >= route.Count)
        {
            return null;
        }

        return route[nextIndex];
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

        // まず次のWaypointを予約する。
        // 予約できない場合は進まない。
        if (!TryReserveMoveToWaypoint(targetWaypoint))
        {
            StopCarCompletely();
            return;
        }

        Vector3 targetPosition = targetWaypoint.transform.position;
        targetPosition.y = transform.position.y;

        // 大きく曲がる必要がある場合は、Waypoint上で停止してその場回転する。
        // その場回転中は前方検知を呼ばない。
        if (TryHandleTurnInPlaceBeforeMove(targetPosition))
        {
            return;
        }

        // 回転が完了した後、通常の前方検知を行ってから発進する。
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
            Waypoint nextWaypoint = null;

            if (currentWaypointIndex + 1 < route.Count)
            {
                nextWaypoint = route[currentWaypointIndex + 1];
            }

            ArriveAtWaypoint(targetWaypoint, nextWaypoint);
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

        if (targetParkingSlot.reservedBy != null && targetParkingSlot.reservedBy != this)
        {
            Debug.LogWarning($"{name}: 目的Slotは別の車が予約しています。Slot={targetParkingSlot.slotId}");
            FinishDriving();
            return;
        }

        if (targetParkingSlot.occupiedBy != null && targetParkingSlot.occupiedBy != this)
        {
            Debug.LogWarning($"{name}: 目的Slotは別の車が使用中です。Slot={targetParkingSlot.slotId}");
            FinishDriving();
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
        if (targetParkingSlot != null)
        {
            bool occupied = targetParkingSlot.TryOccupy(this);

            if (!occupied)
            {
                Debug.LogWarning($"{name}: 目的ParkingSlotをOccupiedにできませんでした。Slot={targetParkingSlot.slotId}");
                StopCarCompletely();
                moveState = NPC_CarMoveState.Idle;
                return;
            }

            transform.position = new Vector3(
                targetParkingSlot.parkingPoint.position.x,
                transform.position.y,
                targetParkingSlot.parkingPoint.position.z
            );

            transform.rotation = targetParkingSlot.parkingPoint.rotation;

            if (car != null)
            {
                // 駐車完了ログは Car.cs 側で出す前提。
                car.SetParked(targetParkingSlot);
            }
        }

        StopCarCompletely();

        EndTurnInPlaceAtWaypoint();
        ReleaseWaypointReservation();

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
        EndTurnInPlaceAtWaypoint();

        if (car != null)
        {
            car.ClearParked();
        }

        currentExitWaypointIndex = 0;

        if (targetParkingSlot != null && targetParkingSlot.accessWaypoint != null)
        {
            backOutRetryTimer = 0f;

            moveState = useBackOutSafetyCheck
                ? NPC_CarMoveState.WaitingToBackOut
                : NPC_CarMoveState.BackingOut;
        }
        else
        {
            FinishDriving();
        }
    }

    private void WaitUntilCanBackOut()
    {
        StopCarCompletely();

        backOutRetryTimer -= Time.deltaTime;

        if (backOutRetryTimer > 0f)
        {
            return;
        }

        backOutRetryTimer = backOutRetryInterval;

        if (targetParkingSlot == null || targetParkingSlot.accessWaypoint == null)
        {
            FinishDriving();
            return;
        }

        Waypoint accessWaypoint = targetParkingSlot.accessWaypoint;

        // 出庫前にAccessWaypointを予約する。
        // 予約できない場合は、スロット内で待機する。
        if (!TryReserveWaypoint(accessWaypoint))
        {
            SetReservationWait(accessWaypoint, null);

            if (logBackOutSafetyDebug)
            {
                Debug.Log($"{name}: AccessWaypointを予約できないため、出庫待機します。Waypoint={accessWaypoint.name}", this);
            }

            return;
        }

        ClearReservationWait();

        // 予約できた後、物理的にバックできるか確認する。
        if (CanBackOutSafely())
        {
            if (logBackOutSafetyDebug)
            {
                Debug.Log($"{name}: 出庫確認OK。バックを開始します。");
            }

            moveState = NPC_CarMoveState.BackingOut;
        }
        else
        {
            if (logBackOutSafetyDebug)
            {
                Debug.Log($"{name}: AccessWaypoint付近に車がいるため、出庫待機します。");
            }
        }
    }

    private bool CanBackOutSafely()
    {
        return !TryGetBackOutBlockingCar(out _);
    }

    private void BackOutFromParkingSlot()
    {
        if (targetParkingSlot == null || targetParkingSlot.accessWaypoint == null)
        {
            FinishDriving();
            return;
        }

        // バック中にもAccessWaypoint付近を確認する。
        // 別の車が来た場合は、バック動作を中断して再待機する。
        if (checkWhileBackingOut && TryGetBackOutBlockingCar(out NPC_CarController blockingCar))
        {
            StopCarCompletely();

            RegisterBackOutYield(blockingCar);

            backOutRetryTimer = backOutRetryInterval;

            if (logBackOutSafetyDebug)
            {
                Debug.Log($"{name}: バック中に別の車を検知したため、出庫を一時中断します。BlockingCar={blockingCar.name}", this);
            }

            moveState = NPC_CarMoveState.WaitingToBackOut;
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

            Waypoint nextWaypoint = null;

            if (exitRoute != null && currentExitWaypointIndex < exitRoute.Count)
            {
                nextWaypoint = exitRoute[currentExitWaypointIndex];
            }

            ArriveAtWaypoint(backOutWaypoint, nextWaypoint);

            ParkingSlot releasedSlot = targetParkingSlot;
            bool released = releasedSlot.TryReleaseAfterExit(this);

            if (!released)
            {
                Debug.LogWarning(
                    $"{name}: スロット解放に失敗しました。Slot={releasedSlot.slotId}, " +
                    $"State={releasedSlot.state}, " +
                    $"ReservedBy={(releasedSlot.reservedBy != null ? releasedSlot.reservedBy.name : "null")}, " +
                    $"OccupiedBy={(releasedSlot.occupiedBy != null ? releasedSlot.occupiedBy.name : "null")}",
                    this
                );
            }

            targetParkingSlot = null;

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

    private void RegisterBackOutYield(NPC_CarController blockingCar)
    {
        if (!useBackOutYieldToPassingCar)
        {
            return;
        }

        if (blockingCar == null)
        {
            return;
        }

        if (blockingCar == this)
        {
            return;
        }

        yieldingToCar = blockingCar;
        backOutYieldTimer = backOutYieldMemoryTime;

        if (logBackOutSafetyDebug)
        {
            Debug.Log($"{name}: バック中断。{blockingCar.name} に道を譲ります。", this);
        }
    }

    public bool IsYieldingBackOutTo(NPC_CarController otherCar)
    {
        if (!useBackOutYieldToPassingCar)
        {
            return false;
        }

        if (otherCar == null)
        {
            return false;
        }

        if (moveState != NPC_CarMoveState.WaitingToBackOut)
        {
            return false;
        }

        return yieldingToCar == otherCar && backOutYieldTimer > 0f;
    }

    private bool TryGetBackOutBlockingCar(out NPC_CarController blockingCar)
    {
        blockingCar = null;

        if (!useBackOutSafetyCheck)
        {
            return false;
        }

        if (targetParkingSlot == null || targetParkingSlot.accessWaypoint == null)
        {
            return true;
        }

        Transform checkTransform = targetParkingSlot.accessWaypoint.transform;
        Vector3 checkCenter = checkTransform.TransformPoint(backOutCheckCenterOffset);

        Collider[] hits = Physics.OverlapBox(
            checkCenter,
            backOutCheckBoxSize * 0.5f,
            checkTransform.rotation,
            backOutCheckLayerMask
        );

        foreach (Collider hit in hits)
        {
            Car hitCar = hit.GetComponentInParent<Car>();

            if (hitCar == null)
            {
                continue;
            }

            // 自分自身は無視。
            if (hitCar == car)
            {
                continue;
            }

            NPC_CarController hitController = hit.GetComponentInParent<NPC_CarController>();

            if (hitController != null)
            {
                // 駐車中の車を無視する設定がONの場合のみ、
                // 向かい側スロットなどに駐車している車を無視する。
                if (ignoreParkedCarsInBackOutCheck)
                {
                    if (hitController.moveState == NPC_CarMoveState.Parked ||
                        hitController.moveState == NPC_CarMoveState.Finished ||
                        hitCar.isParked ||
                        hitCar.currentParkingSlot != null)
                    {
                        continue;
                    }
                }

                blockingCar = hitController;
                return true;
            }

            // NPC_CarController が付いていないCarも、駐車中なら必要に応じて無視する。
            if (ignoreParkedCarsInBackOutCheck)
            {
                if (hitCar.isParked || hitCar.currentParkingSlot != null)
                {
                    continue;
                }
            }

            return true;
        }

        return false;
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

        // 旋回前に、次の出口Waypointを予約する。
        if (!TryReserveMoveToWaypoint(nextWaypoint))
        {
            StopCarCompletely();
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

        // 次の出口WaypointとRoadSectionを予約する。
        if (!TryReserveMoveToWaypoint(targetWaypoint))
        {
            StopCarCompletely();
            return;
        }

        Vector3 targetPosition = targetWaypoint.transform.position;
        targetPosition.y = transform.position.y;

        if (TryHandleTurnInPlaceBeforeMove(targetPosition))
        {
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
            Waypoint nextWaypoint = null;

            if (currentExitWaypointIndex + 1 < exitRoute.Count)
            {
                nextWaypoint = exitRoute[currentExitWaypointIndex + 1];
            }

            ArriveAtWaypoint(targetWaypoint, nextWaypoint);
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
    private bool TryReserveWaypoint(Waypoint waypoint)
    {
        if (!useWaypointReservation)
        {
            return true;
        }

        if (waypoint == null)
        {
            return false;
        }

        // すでに自分が予約中、または占有中ならそのまま通す。
        if (reservedWaypoint == waypoint || occupiedWaypoint == waypoint)
        {
            return true;
        }

        bool reserved = waypoint.TryReserve(this);

        if (!reserved)
        {
            if (logWaypointReservationDebug)
            {
                Debug.Log($"{name}: Waypointを予約できないため待機します。Waypoint={waypoint.name}", this);
            }

            return false;
        }

        // 以前の予約が残っていれば解放する。
        // ただし、現在占有中のWaypointはここでは解放しない。
        if (reservedWaypoint != null && reservedWaypoint != occupiedWaypoint)
        {
            reservedWaypoint.Release(this);
        }

        reservedWaypoint = waypoint;

        if (logWaypointReservationDebug)
        {
            Debug.Log($"{name}: Waypointを予約しました。Waypoint={waypoint.name}", this);
        }

        return true;
    }

    private RoadSection GetRoadSectionToWaypoint(Waypoint targetWaypoint)
    {
        if (occupiedWaypoint == null)
        {
            return null;
        }

        return occupiedWaypoint.GetRoadSectionTo(targetWaypoint);
    }

    private bool TryReserveRoadSection(RoadSection roadSection)
    {
        if (!useWaypointReservation)
        {
            return true;
        }

        if (roadSection == null)
        {
            return true;
        }

        if (reservedRoadSection == roadSection)
        {
            return true;
        }

        bool reserved = roadSection.TryReserve(this);

        if (!reserved)
        {
            if (logWaypointReservationDebug)
            {
                Debug.Log($"{name}: RoadSectionを予約できないため待機します。RoadSection={roadSection.name}", this);
            }

            return false;
        }

        if (reservedRoadSection != null && reservedRoadSection != roadSection)
        {
            reservedRoadSection.Release(this);
        }

        reservedRoadSection = roadSection;

        if (logWaypointReservationDebug)
        {
            Debug.Log($"{name}: RoadSectionを予約しました。RoadSection={roadSection.name}", this);
        }

        return true;
    }

    private void ReleaseRoadSectionReservation()
    {
        if (reservedRoadSection != null)
        {
            reservedRoadSection.Release(this);
            reservedRoadSection = null;
        }
    }

    private bool TryReserveMoveToWaypoint(Waypoint targetWaypoint)
    {
        if (!useWaypointReservation)
        {
            ClearReservationWait();
            return true;
        }

        if (targetWaypoint == null)
        {
            SetReservationWait(null, null);
            return false;
        }

        RoadSection roadSection = GetRoadSectionToWaypoint(targetWaypoint);

        if (!TryReserveRoadSection(roadSection))
        {
            SetReservationWait(targetWaypoint, roadSection);
            return false;
        }

        if (!TryReserveWaypoint(targetWaypoint))
        {
            SetReservationWait(targetWaypoint, roadSection);

            if (roadSection != null && reservedRoadSection == roadSection)
            {
                roadSection.Release(this);
                reservedRoadSection = null;
            }

            return false;
        }

        ClearReservationWait();
        return true;
    }

    private RoadSection GetRoadSectionBetween(Waypoint from, Waypoint to)
    {
        if (from == null || to == null)
        {
            return null;
        }

        return from.GetRoadSectionTo(to);
    }

    private bool ShouldKeepCurrentRoadSection(Waypoint arrivedWaypoint, Waypoint nextWaypoint)
    {
        if (reservedRoadSection == null)
        {
            return false;
        }

        if (arrivedWaypoint == null || nextWaypoint == null)
        {
            return false;
        }

        RoadSection nextRoadSection = GetRoadSectionBetween(arrivedWaypoint, nextWaypoint);

        return nextRoadSection == reservedRoadSection;
    }

    private void SetReservationWait(Waypoint waypoint, RoadSection roadSection)
    {
        isStoppedByReservation = true;
        waitingWaypoint = waypoint;
        waitingRoadSection = roadSection;

        if (logWaypointReservationDebug)
        {
            string waypointName = waypoint != null ? waypoint.name : "null";
            string roadSectionName = roadSection != null ? roadSection.name : "null";

            Debug.Log($"{name}: 予約待機中。Waypoint={waypointName}, RoadSection={roadSectionName}", this);
        }
    }

    private void ClearReservationWait()
    {
        isStoppedByReservation = false;
        waitingWaypoint = null;
        waitingRoadSection = null;
    }

    public bool IsWaitingForRoadSection(RoadSection roadSection)
    {
        if (roadSection == null)
        {
            return false;
        }

        return isStoppedByReservation && waitingRoadSection == roadSection;
    }

    public bool IsStoppedByReservation()
    {
        return isStoppedByReservation;
    }

    private void ArriveAtWaypoint(Waypoint waypoint, Waypoint nextWaypoint)
    {
        if (!useWaypointReservation)
        {
            occupiedWaypoint = waypoint;
            reservedWaypoint = null;
            reservedRoadSection = null;
            return;
        }

        if (waypoint == null)
        {
            return;
        }

        if (occupiedWaypoint != null && occupiedWaypoint != waypoint)
        {
            occupiedWaypoint.Release(this);
        }

        waypoint.Enter(this);
        occupiedWaypoint = waypoint;

        if (reservedWaypoint == waypoint)
        {
            reservedWaypoint = null;
        }

        // 次の区間も同じRoadSectionなら、まだ解放しない。
        if (!ShouldKeepCurrentRoadSection(waypoint, nextWaypoint))
        {
            ReleaseRoadSectionReservation();
        }

        if (logWaypointReservationDebug)
        {
            Debug.Log($"{name}: Waypointに到着しました。Waypoint={waypoint.name}", this);
        }
    }

    private void ReleaseWaypointReservation()
    {
        ReleaseRoadSectionReservation();

        if (reservedWaypoint != null)
        {
            reservedWaypoint.Release(this);
            reservedWaypoint = null;
        }

        if (occupiedWaypoint != null)
        {
            occupiedWaypoint.Release(this);
            occupiedWaypoint = null;
        }

        ClearReservationWait();
    }

    private bool TryHandleTurnInPlaceBeforeMove(Vector3 targetPosition)
    {
        if (!useTurnInPlaceAtWaypoint)
        {
            return false;
        }

        if (moveState != NPC_CarMoveState.DrivingRoute &&
            moveState != NPC_CarMoveState.Leaving)
        {
            EndTurnInPlaceAtWaypoint();
            return false;
        }

        Vector3 directionToTarget = targetPosition - transform.position;
        directionToTarget.y = 0f;

        if (directionToTarget.sqrMagnitude <= 0.001f)
        {
            EndTurnInPlaceAtWaypoint();
            return false;
        }

        Vector3 forward = transform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude <= 0.001f)
        {
            EndTurnInPlaceAtWaypoint();
            return false;
        }

        float currentAngle = Vector3.Angle(
            forward.normalized,
            directionToTarget.normalized
        );

        // まだその場回転を始めていない場合、
        // 角度が小さければ通常走行する。
        if (!isTurningInPlaceAtWaypoint && currentAngle < turnInPlaceStartAngle)
        {
            return false;
        }

        // ここからはその場回転中。
        // 前方検知は呼ばず、完全停止して回転だけ行う。
        StopCarCompletely();

        if (!isTurningInPlaceAtWaypoint)
        {
            isTurningInPlaceAtWaypoint = true;
            turnInPlacePauseTimer = turnInPlacePauseTime;

            if (logTurnInPlaceDebug)
            {
                Debug.Log($"{name}: Waypointでその場回転を開始します。Angle={currentAngle:F1}", this);
            }
        }

        // 一瞬停止してから回転する。
        if (turnInPlacePauseTimer > 0f)
        {
            turnInPlacePauseTimer -= Time.deltaTime;
            return true;
        }

        Quaternion targetRotation = Quaternion.LookRotation(
            directionToTarget.normalized,
            Vector3.up
        );

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotateSpeed * Time.deltaTime
        );

        float remainingAngle = Quaternion.Angle(transform.rotation, targetRotation);

        if (remainingAngle <= turnInPlaceCompleteAngle)
        {
            transform.rotation = targetRotation;
            EndTurnInPlaceAtWaypoint();

            if (logTurnInPlaceDebug)
            {
                Debug.Log($"{name}: Waypointでのその場回転が完了しました。", this);
            }

            // このフレームではまだ移動しない。
            // 次のUpdateで前方検知を通常通り行ってから発進する。
            return true;
        }

        return true;
    }

    private void EndTurnInPlaceAtWaypoint()
    {
        isTurningInPlaceAtWaypoint = false;
        turnInPlacePauseTimer = 0f;
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
        if (frontRayIgnoreTimer > 0f)
        {
            isStoppedByFrontCar = false;
            lastDetectedFrontCarController = null;
            return false;
        }

        if (!useFrontVehicleDetection)
        {
            isStoppedByFrontCar = false;
            lastDetectedFrontCarController = null;
            return false;
        }

        if (frontSensor == null)
        {
            isStoppedByFrontCar = false;
            lastDetectedFrontCarController = null;
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

            NPC_CarController hitCarController = hit.collider.GetComponentInParent<NPC_CarController>();

            if (hitCarController != null)
            {
                if (ShouldIgnoreCarForFrontDetection(hitCarController))
                {
                    continue;
                }

                lastDetectedFrontCarController = hitCarController;

                if (TryResolveFrontConflict(hitCarController))
                {
                    isStoppedByFrontCar = false;
                    return false;
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
        lastDetectedFrontCarController = null;
        return false;
    }

    private bool ShouldIgnoreCarForFrontDetection(NPC_CarController otherCarController)
    {
        if (otherCarController == null)
        {
            return false;
        }

        if (otherCarController.moveState == NPC_CarMoveState.Parked ||
            otherCarController.moveState == NPC_CarMoveState.Finished)
        {
            return true;
        }

        if (otherCarController.IsYieldingBackOutTo(this))
        {
            return true;
        }

        // 自分がRoadSectionを予約して通過中の場合、
        // そのRoadSectionに入れず待っている車は前方検知から外す。
        if (reservedRoadSection != null)
        {
            if (otherCarController.IsWaitingForRoadSection(reservedRoadSection))
            {
                if (logFrontVehicleDetection)
                {
                    Debug.Log($"{name}: 同じRoadSection待機中の車を前方検知から除外します。Other={otherCarController.name}, RoadSection={reservedRoadSection.name}", this);
                }

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
        EndTurnInPlaceAtWaypoint();
        ReleaseWaypointReservation();

        if (targetParkingSlot != null)
        {
            ParkingSlot releasedSlot = targetParkingSlot;
            bool released = releasedSlot.TryReleaseAfterExit(this);

            if (!released)
            {
                Debug.LogWarning(
                    $"{name}: FinishDriving時のSlot解放に失敗しました。Slot={releasedSlot.slotId}, " +
                    $"State={releasedSlot.state}, " +
                    $"ReservedBy={(releasedSlot.reservedBy != null ? releasedSlot.reservedBy.name : "null")}, " +
                    $"OccupiedBy={(releasedSlot.occupiedBy != null ? releasedSlot.occupiedBy.name : "null")}",
                    this
                );
            }

            targetParkingSlot = null;
        }

        StopCarCompletely();

        moveState = NPC_CarMoveState.Finished;

        if (destroyOnFinished)
        {
            Destroy(gameObject);
        }
    }

    private bool TryResolveFrontConflict(NPC_CarController otherCar)
    {
        if (!useTemporaryFrontRayIgnore)
        {
            return false;
        }

        if (otherCar == null)
        {
            return false;
        }

        if (otherCar == this)
        {
            return false;
        }

        if (!allowCrossDirectionDeadlockResolve)
        {
            return false;
        }

        if (!IsCloseEnoughForDeadlock(otherCar))
        {
            return false;
        }

        if (!IsValidDeadlockTarget(otherCar))
        {
            return false;
        }

        bool otherIsAlsoStopped =
            otherCar.isStoppedByFrontCar ||
            otherCar.lastDetectedFrontCarController == this ||
            otherCar.IsStoppedByReservation();

        if (!otherIsAlsoStopped)
        {
            return false;
        }

        // 両方が同時に無効化すると危険なので、
        // InstanceIDで必ず片方だけがレイを一時無効化する。
        if (!ShouldThisCarIgnoreFrontRay(otherCar))
        {
            return false;
        }

        frontRayIgnoreTimer = frontRayIgnoreDuration;
        isStoppedByFrontCar = false;

        if (logDeadlockResolve)
        {
            Debug.Log(
                $"{name}: 鉢合わせ停止を解消するため、一時的に前方レイを無効化します。Opponent={otherCar.name}",
                this
            );
        }

        return true;
    }

    private bool IsCloseEnoughForDeadlock(NPC_CarController otherCar)
    {
        if (otherCar == null)
        {
            return false;
        }

        float distance = Vector3.Distance(transform.position, otherCar.transform.position);

        return distance <= deadlockCheckDistance;
    }

    private bool IsValidDeadlockTarget(NPC_CarController otherCar)
    {
        if (otherCar == null)
        {
            return false;
        }

        if (otherCar.moveState == NPC_CarMoveState.Parked ||
            otherCar.moveState == NPC_CarMoveState.Finished)
        {
            return false;
        }

        return true;
    }

    private bool ShouldThisCarIgnoreFrontRay(NPC_CarController otherCar)
    {
        if (otherCar == null)
        {
            return false;
        }

        // InstanceIDが小さい方だけが一時的に進む。
        // これにより、両方が同時にレイ無効化して突っ込むのを防ぐ。
        return GetInstanceID() < otherCar.GetInstanceID();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawBackOutCheckGizmo)
        {
            return;
        }

        if (targetParkingSlot == null || targetParkingSlot.accessWaypoint == null)
        {
            return;
        }

        Transform checkTransform = targetParkingSlot.accessWaypoint.transform;

        Gizmos.color = Color.magenta;

        Matrix4x4 oldMatrix = Gizmos.matrix;

        Vector3 checkCenter = checkTransform.TransformPoint(backOutCheckCenterOffset);

        Gizmos.matrix = Matrix4x4.TRS(
            checkCenter,
            checkTransform.rotation,
            backOutCheckBoxSize
        );

        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

        Gizmos.matrix = oldMatrix;
    }
}
