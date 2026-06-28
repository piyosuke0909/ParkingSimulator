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


    [Header("Front Vehicle Detection")]
    public bool useFrontVehicleDetection = true;
    public Transform frontSensor;
    public LayerMask carLayerMask;
    public float frontCheckDistance = 15f;
    public float frontCheckRadius = 1.8f;
    public float frontStopHoldTime = 0.5f;
    public bool drawFrontCheckDebug = true;
    public bool logFrontVehicleDetection = false;

    [Header("Start Move Safety Check")]
    [Tooltip("停止状態から発進する直前に、前方を広めのBoxで確認します。")]
    public bool useStartMoveSafetyCheck = true;

    [Tooltip("発進前確認Boxの大きさです。Xは横幅、Yは高さ、Zは前方距離です。")]
    public Vector3 startMoveCheckBoxSize = new Vector3(10f, 4f, 8f);

    [Tooltip("発進前確認Boxの中心位置補正です。Zを前方へずらします。")]
    public Vector3 startMoveCheckCenterOffset = new Vector3(0f, 1f, 4f);

    [Tooltip("発進前確認BoxのGizmoを表示します。")]
    public bool drawStartMoveCheckGizmo = true;

    [Tooltip("発進前確認のログを出します。")]
    public bool logStartMoveSafetyCheck = false;

    [Header("Turn In Place At Waypoint")]
    [Tooltip("Waypointで大きく方向転換する場合、一度停止してからその場で回転します。")]
    public bool useTurnInPlaceAtWaypoint = true;

    [Tooltip("この角度以上向きがずれている場合、その場回転を行います。")]
    public float turnInPlaceStartAngle = 35f;

    [Tooltip("この角度以下になったら、その場回転完了とします。")]
    public float turnInPlaceCompleteAngle = 3f;

    [Tooltip("Waypoint到着後、回転を始める前に停止する時間です。")]
    public float turnInPlacePauseTime = 0.15f;

    [Tooltip("その場回転の回転速度です。1秒あたりの角度です。")]
    public float turnInPlaceRotateSpeed = 240f;

    [Tooltip("この距離以内のWaypointは、その場回転の目標にしません。近すぎるWaypointを向こうとして余計に回るのを防ぎます。")]
    public float turnInPlaceMinTargetDistance = 1.0f;

    [Tooltip("その場回転中のログを出します。")]
    public bool logTurnInPlaceDebug = false;

    private bool isTurningInPlaceAtWaypoint;
    private float turnInPlacePauseTimer;
    private Quaternion turnInPlaceTargetRotation = Quaternion.identity;
    private bool hasTurnInPlaceTargetRotation;


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
    private TrafficBlock reservedTrafficBlock;
    private TrafficBlock occupiedTrafficBlock;
    private bool isStoppedByReservation;
    private Waypoint waitingWaypoint;
    private RoadSection waitingRoadSection;
    private TrafficBlock waitingTrafficBlock;

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
        if (TryHandleTurnInPlaceBeforeMove(targetWaypoint, targetPosition))
        {
            return;
        }

        // 回転が完了した後、通常の前方検知を行ってから発進する。
        if (ShouldHoldStopForFrontCar())
        {
            StopCarCompletely();
            return;
        }

        // 前方レイに加えて、発進直前だけ広めのBoxで安全確認する。
        // スロットから斜めに出てくる車や、レイから外れた車を拾いやすくするため。
        if (ShouldStopForStartMoveSafety())
        {
            StopCarCompletely();
            return;
        }

        MoveToTarget(targetPosition, moveSpeed);
        RotateToMoveDirection(targetWaypoint, targetPosition);

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
            // TryOccupyは予約・所有者情報だけを更新する。Occupied/Emptyの物理判定はCameraParkingSensorが行う。
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

        if (targetParkingSlot != null)
        {
            // 空き判定はCameraParkingSensorに任せる。
            // ここでは「この車が出庫中で、まだ他の車は予約できない」ことだけを記録する。
            targetParkingSlot.SetLeaving(this);
        }

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

        // 出庫前にAccessWaypoint / TrafficBlock / RoadSectionを予約する。
        // 予約できない場合は、スロット内で待機する。
        if (!TryReserveMoveToWaypoint(accessWaypoint))
        {
            if (logBackOutSafetyDebug)
            {
                Debug.Log($"{name}: AccessWaypoint、TrafficBlock、またはRoadSectionを予約できないため、出庫待機します。Waypoint={accessWaypoint.name}", this);
            }

            return;
        }

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
            // TryReleaseAfterExitは予約・所有者情報だけを解放する。Empty判定はCameraParkingSensorが行う。
            // TryReleaseAfterExitは予約・所有者情報だけを解放する。Empty判定はCameraParkingSensorが行う。
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
                moveState = NPC_CarMoveState.Leaving;
            }
        }
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

        if (TryHandleTurnInPlaceBeforeMove(targetWaypoint, targetPosition))
        {
            return;
        }

        if (ShouldHoldStopForFrontCar())
        {
            StopCarCompletely();
            return;
        }

        // 前方レイに加えて、発進直前だけ広めのBoxで安全確認する。
        // スロットから斜めに出てくる車や、レイから外れた車を拾いやすくするため。
        if (ShouldStopForStartMoveSafety())
        {
            StopCarCompletely();
            return;
        }

        MoveToTarget(targetPosition, moveSpeed);
        RotateToMoveDirection(targetWaypoint, targetPosition);

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

    private TrafficBlock GetTrafficBlockForWaypoint(Waypoint waypoint)
    {
        if (waypoint == null)
        {
            return null;
        }

        return waypoint.trafficBlock;
    }

    private bool TryReserveTrafficBlock(TrafficBlock trafficBlock)
    {
        if (!useWaypointReservation)
        {
            return true;
        }

        if (trafficBlock == null)
        {
            return true;
        }

        if (reservedTrafficBlock == trafficBlock || occupiedTrafficBlock == trafficBlock)
        {
            return true;
        }

        bool reserved = trafficBlock.TryReserve(this);

        if (!reserved)
        {
            if (logWaypointReservationDebug)
            {
                Debug.Log($"{name}: TrafficBlockを予約できないため待機します。TrafficBlock={trafficBlock.name}", this);
            }

            return false;
        }

        if (reservedTrafficBlock != null &&
            reservedTrafficBlock != trafficBlock &&
            reservedTrafficBlock != occupiedTrafficBlock)
        {
            reservedTrafficBlock.Release(this);
        }

        reservedTrafficBlock = trafficBlock;

        if (logWaypointReservationDebug)
        {
            Debug.Log($"{name}: TrafficBlockを予約しました。TrafficBlock={trafficBlock.name}", this);
        }

        return true;
    }

    private void ReleaseTrafficBlockReservation()
    {
        if (reservedTrafficBlock != null)
        {
            reservedTrafficBlock.Release(this);
            reservedTrafficBlock = null;
        }
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
            SetReservationWait(null, null, null);
            return false;
        }

        TrafficBlock trafficBlock = GetTrafficBlockForWaypoint(targetWaypoint);
        RoadSection roadSection = GetRoadSectionToWaypoint(targetWaypoint);

        // 1. 場所・危険エリアの予約
        if (!TryReserveTrafficBlock(trafficBlock))
        {
            SetReservationWait(targetWaypoint, roadSection, trafficBlock);
            return false;
        }

        // 2. 道・区間の予約
        if (!TryReserveRoadSection(roadSection))
        {
            SetReservationWait(targetWaypoint, roadSection, trafficBlock);

            if (trafficBlock != null &&
                reservedTrafficBlock == trafficBlock &&
                occupiedTrafficBlock != trafficBlock)
            {
                trafficBlock.Release(this);
                reservedTrafficBlock = null;
            }

            return false;
        }

        // 3. 次Waypointそのものの予約
        if (!TryReserveWaypoint(targetWaypoint))
        {
            SetReservationWait(targetWaypoint, roadSection, trafficBlock);

            if (roadSection != null && reservedRoadSection == roadSection)
            {
                roadSection.Release(this);
                reservedRoadSection = null;
            }

            if (trafficBlock != null &&
                reservedTrafficBlock == trafficBlock &&
                occupiedTrafficBlock != trafficBlock)
            {
                trafficBlock.Release(this);
                reservedTrafficBlock = null;
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

    private void SetReservationWait(Waypoint waypoint, RoadSection roadSection, TrafficBlock trafficBlock)
    {
        isStoppedByReservation = true;
        waitingWaypoint = waypoint;
        waitingRoadSection = roadSection;
        waitingTrafficBlock = trafficBlock;

        if (logWaypointReservationDebug)
        {
            string waypointName = waypoint != null ? waypoint.name : "null";
            string roadSectionName = roadSection != null ? roadSection.name : "null";
            string trafficBlockName = trafficBlock != null ? trafficBlock.name : "null";

            Debug.Log($"{name}: 予約待機中。Waypoint={waypointName}, RoadSection={roadSectionName}, TrafficBlock={trafficBlockName}", this);
        }
    }

    private void ClearReservationWait()
    {
        isStoppedByReservation = false;
        waitingWaypoint = null;
        waitingRoadSection = null;
        waitingTrafficBlock = null;
    }

    public bool IsWaitingForRoadSection(RoadSection roadSection)
    {
        if (roadSection == null)
        {
            return false;
        }

        return isStoppedByReservation && waitingRoadSection == roadSection;
    }

    public bool IsWaitingForTrafficBlock(TrafficBlock trafficBlock)
    {
        if (trafficBlock == null)
        {
            return false;
        }

        return isStoppedByReservation && waitingTrafficBlock == trafficBlock;
    }

    public bool IsWaitingForWaypoint(Waypoint waypoint)
    {
        if (waypoint == null)
        {
            return false;
        }

        return isStoppedByReservation && waitingWaypoint == waypoint;
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
            reservedTrafficBlock = null;
            occupiedTrafficBlock = GetTrafficBlockForWaypoint(waypoint);
            return;
        }

        if (waypoint == null)
        {
            return;
        }

        TrafficBlock previousTrafficBlock = occupiedTrafficBlock;
        TrafficBlock newTrafficBlock = GetTrafficBlockForWaypoint(waypoint);

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

        if (newTrafficBlock != null)
        {
            newTrafficBlock.Enter(this);

            if (reservedTrafficBlock == newTrafficBlock)
            {
                reservedTrafficBlock = null;
            }
        }

        occupiedTrafficBlock = newTrafficBlock;

        if (previousTrafficBlock != null && previousTrafficBlock != newTrafficBlock)
        {
            previousTrafficBlock.Release(this);
        }

        // 次の区間も同じRoadSectionなら、まだ解放しない。
        if (!ShouldKeepCurrentRoadSection(waypoint, nextWaypoint))
        {
            ReleaseRoadSectionReservation();
        }

        if (logWaypointReservationDebug)
        {
            string blockName = newTrafficBlock != null ? newTrafficBlock.name : "null";
            Debug.Log($"{name}: Waypointに到着しました。Waypoint={waypoint.name}, TrafficBlock={blockName}", this);
        }
    }

    private void ReleaseWaypointReservation()
    {
        ReleaseRoadSectionReservation();
        ReleaseTrafficBlockReservation();

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

        if (occupiedTrafficBlock != null)
        {
            occupiedTrafficBlock.Release(this);
            occupiedTrafficBlock = null;
        }

        ClearReservationWait();
    }

    private bool TryGetStableMoveDirection(Waypoint targetWaypoint, Vector3 targetPosition, out Vector3 direction)
    {
        direction = Vector3.zero;

        // まず「現在占有中のWaypoint → 次のWaypoint」の方向を使う。
        // 車の現在位置から次Waypointを見るよりも、道路の線分方向に揃いやすい。
        if (occupiedWaypoint != null && targetWaypoint != null && occupiedWaypoint != targetWaypoint)
        {
            direction = targetWaypoint.transform.position - occupiedWaypoint.transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.001f)
            {
                direction.Normalize();
                return true;
            }
        }

        // occupiedWaypoint が使えない場合の保険。
        direction = targetPosition - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
        {
            return false;
        }

        direction.Normalize();
        return true;
    }

    private float GetHorizontalDistanceToTarget(Vector3 targetPosition)
    {
        Vector3 diff = targetPosition - transform.position;
        diff.y = 0f;
        return diff.magnitude;
    }

    private bool TryHandleTurnInPlaceBeforeMove(Waypoint targetWaypoint, Vector3 targetPosition)
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

        float targetDistance = GetHorizontalDistanceToTarget(targetPosition);

        // 近すぎるWaypointを向こうとすると、背後や真横を目標にして余計に回りやすい。
        // ただし、ここではWaypoint到着扱いにはしない。RS状態を壊さないため。
        if (targetDistance <= turnInPlaceMinTargetDistance)
        {
            EndTurnInPlaceAtWaypoint();
            return false;
        }

        if (!TryGetStableMoveDirection(targetWaypoint, targetPosition, out Vector3 directionToTarget))
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
            directionToTarget
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

            // 回転開始時に目標回転を1回だけ固定する。
            // 毎フレーム作り直すと、Waypoint付近で目標方向がブレて余計に回る。
            turnInPlaceTargetRotation = Quaternion.LookRotation(
                directionToTarget,
                Vector3.up
            );

            hasTurnInPlaceTargetRotation = true;

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

        if (!hasTurnInPlaceTargetRotation)
        {
            EndTurnInPlaceAtWaypoint();
            return false;
        }

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            turnInPlaceTargetRotation,
            turnInPlaceRotateSpeed * Time.deltaTime
        );

        float remainingAngle = Quaternion.Angle(
            transform.rotation,
            turnInPlaceTargetRotation
        );

        if (remainingAngle <= turnInPlaceCompleteAngle)
        {
            transform.rotation = turnInPlaceTargetRotation;
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
        hasTurnInPlaceTargetRotation = false;
        turnInPlaceTargetRotation = Quaternion.identity;
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

            NPC_CarController hitCarController = hit.collider.GetComponentInParent<NPC_CarController>();

            if (hitCarController != null)
            {
                if (ShouldIgnoreCarForFrontDetection(hitCarController))
                {
                    continue;
                }
            }

            if (logFrontVehicleDetection)
            {
                Debug.Log($"{name}: 前方車両を検知しました → {hitCar.carId}", this);
            }

            isStoppedByFrontCar = true;
            return true;
        }

        isStoppedByFrontCar = false;
        return false;
    }

    private bool ShouldStopForStartMoveSafety()
    {
        if (!useStartMoveSafetyCheck)
        {
            return false;
        }

        Vector3 checkCenter = transform.TransformPoint(startMoveCheckCenterOffset);

        Collider[] hits = Physics.OverlapBox(
            checkCenter,
            startMoveCheckBoxSize * 0.5f,
            transform.rotation,
            carLayerMask
        );

        foreach (Collider hit in hits)
        {
            Car hitCar = hit.GetComponentInParent<Car>();

            if (hitCar == null)
            {
                continue;
            }

            if (hitCar == car)
            {
                continue;
            }

            NPC_CarController hitController = hit.GetComponentInParent<NPC_CarController>();

            if (hitController != null)
            {
                if (ShouldIgnoreCarForFrontDetection(hitController))
                {
                    continue;
                }
            }

            if (logStartMoveSafetyCheck)
            {
                Debug.Log($"{name}: 発進前確認で車を検知したため停止します。Other={hitCar.carId}", this);
            }

            isStoppedByFrontCar = true;
            return true;
        }

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


        // 自分が予約済みのWaypointに入れず待っている車は前方検知から外す。
        // RoadSection未設定の通路では、この判定が特に重要。
        // 例: 自分が●2を予約済み、相手が●2を予約できず待機している場合。
        if (reservedWaypoint != null)
        {
            if (otherCarController.IsWaitingForWaypoint(reservedWaypoint))
            {
                if (logFrontVehicleDetection)
                {
                    Debug.Log($"{name}: 同じWaypoint待機中の車を前方検知から除外します。Other={otherCarController.name}, Waypoint={reservedWaypoint.name}", this);
                }

                return true;
            }
        }

        // 自分がTrafficBlockを予約・占有している場合、
        // そのTrafficBlockに入れず待っている車は前方検知から外す。
        if (reservedTrafficBlock != null)
        {
            if (otherCarController.IsWaitingForTrafficBlock(reservedTrafficBlock))
            {
                if (logFrontVehicleDetection)
                {
                    Debug.Log($"{name}: 同じTrafficBlock待機中の車を前方検知から除外します。Other={otherCarController.name}, TrafficBlock={reservedTrafficBlock.name}", this);
                }

                return true;
            }
        }

        if (occupiedTrafficBlock != null)
        {
            if (otherCarController.IsWaitingForTrafficBlock(occupiedTrafficBlock))
            {
                if (logFrontVehicleDetection)
                {
                    Debug.Log($"{name}: 自分が占有中のTrafficBlock待機車を前方検知から除外します。Other={otherCarController.name}, TrafficBlock={occupiedTrafficBlock.name}", this);
                }

                return true;
            }
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
        direction.y = 0f;

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

    private void RotateToMoveDirection(Waypoint targetWaypoint, Vector3 targetPosition)
    {
        // 通常走行中も、現在位置からWaypointを見るのではなく、
        // occupiedWaypoint → targetWaypoint の道路方向に合わせる。
        if (!TryGetStableMoveDirection(targetWaypoint, targetPosition, out Vector3 direction))
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(
            direction,
            Vector3.up
        );

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            turnInPlaceRotateSpeed * Time.deltaTime
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


    private void OnDrawGizmosSelected()
    {
        if (drawBackOutCheckGizmo &&
            targetParkingSlot != null &&
            targetParkingSlot.accessWaypoint != null)
        {
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

        if (drawStartMoveCheckGizmo)
        {
            Gizmos.color = Color.yellow;

            Matrix4x4 oldMatrix = Gizmos.matrix;

            Vector3 checkCenter = transform.TransformPoint(startMoveCheckCenterOffset);

            Gizmos.matrix = Matrix4x4.TRS(
                checkCenter,
                transform.rotation,
                startMoveCheckBoxSize
            );

            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

            Gizmos.matrix = oldMatrix;
        }
    }
}

