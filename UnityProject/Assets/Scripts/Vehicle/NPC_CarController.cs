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

    [Header("P1 Scenario Speed (Optional)")]
    public bool useScenarioSpeedEffect = true;
    public ScenarioFactorRuntime scenarioFactorRuntime;


    [Header("Parking Wait")]
    public float parkWaitTime = 5f;
    private float parkTimer;

    [Header("Back Out Safety Check")]
    public bool useBackOutSafetyCheck = true;
    public LayerMask backOutCheckLayerMask;
    public Vector3 backOutCheckBoxSize = new Vector3(12f, 4f, 12f);
    public float backOutRetryInterval = 0.5f;
    public bool checkWhileBackingOut = true;

    [Tooltip("バック開始後は出庫車を優先します。道路走行車はBackingOut車を検知して停止するため、出庫車側は通常走行車へ譲り返しません。")]
    public bool committedBackOutHasPriority = true;

    [Tooltip("出庫待ち車が複数いる場合、Instance IDで優先順位を固定して相互待機を防ぎます。")]
    public bool deterministicBackOutPriority = true;

    [Tooltip("WaitingToBackOutはまだ駐車枠内で停止しているため、道路走行車の前方検知から除外します。")]
    public bool ignoreWaitingToBackOutCarsInFrontDetection = true;

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

    [Tooltip("前方車両として扱う同一レーンの左右幅です。隣接レーンの車を拾う場合は小さくしてください。")]
    [Min(0.1f)]
    public float frontSameLaneHalfWidth = 2.75f;

    [Tooltip("進行方向の一致判定です。1で完全に同方向、0で直角、-1で逆方向です。")]
    [Range(-1f, 1f)]
    public float sameDirectionDotThreshold = 0.25f;

    [Tooltip("前方・発進前確認で、隣接レーンや逆向きレーンの車を除外します。")]
    public bool useSameLaneDirectionFilter = true;

    [Tooltip("通常のSphereCast前方検知にも同一レーン・進行方向フィルターを適用します。曲がり角の直交車による循環停止を防ぎます。")]
    public bool applyLaneFilterToPhysicalFrontDetection = true;

    public bool drawFrontCheckDebug = true;
    public bool logFrontVehicleDetection = false;

    [Tooltip("完全に駐車済み（Parked）の車を、通常走行の前方検知と発進前確認から除外します。駐車動作中・出庫待ち・バック中の車は除外しません。")]
    public bool ignoreFullyParkedCarsInFrontDetection = true;

    [Tooltip("駐車済み車を前方検知から除外したときにログを出します。")]
    public bool logIgnoredParkedCars = false;

    [Header("Performance")]
    [Tooltip("前方物理検知を毎フレームではなく一定間隔で実行します。多数車両ではONを推奨します。")]
    public bool usePhysicsQueryThrottling = true;

    [Tooltip("走行可能時の前方物理検知間隔です。0.05秒なら毎秒20回です。")]
    [Min(0.02f)]
    public float movingFrontCheckInterval = 0.05f;

    [Tooltip("前方車両で停止中の再確認間隔です。")]
    [Min(0.02f)]
    public float stoppedFrontCheckInterval = 0.12f;

    [Tooltip("発進前安全確認の再確認間隔です。")]
    [Min(0.02f)]
    public float startMoveSafetyCheckInterval = 0.12f;

    [Tooltip("NonAlloc物理判定用の固定バッファ数です。通常は32で十分です。")]
    [Range(8, 128)]
    public int physicsQueryBufferSize = 32;

    [Tooltip("Play Mode中のDebug.DrawRayを自動的に無効化します。")]
    public bool disableDebugDrawingAtRuntime = true;

    private Collider[] physicsOverlapBuffer;
    private RaycastHit[] physicsCastBuffer;
    private float nextFrontPhysicsCheckAt;
    private float nextStartMoveSafetyCheckAt;
    private bool cachedFrontPhysicsBlocked;
    private bool cachedStartMoveSafetyBlocked;
    private bool physicsBufferOverflowWarned;

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

    [Tooltip("発進前Boxで同一レーンと見なす左右幅です。隣接レーンの車で無駄に止まる場合は小さくします。")]
    public float startMoveSameLaneHalfWidth = 4.25f;

    [Tooltip("この距離より後方にいる車は発進前確認で無視します。")]
    public float startMoveIgnoreBehindDistance = 1.0f;

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


    [Header("Waypoint / Merge Traffic Control")]
    [Tooltip("ONにすると、次Waypointを予約し、駐車レーンから本線へ合流するときだけMergePoint許可を取得します。")]
    public bool useWaypointReservation = true;

    [Tooltip("未設定の場合はシーン内のMergeTrafficCoordinatorを検索します。WaypointやMergePointは自動生成しません。")]
    public MergeTrafficCoordinator mergeTrafficCoordinator;

    [Tooltip("Waypoint・Capacity Area待機のログを出します。")]
    public bool logWaypointReservationDebug = false;

    [Tooltip("SetRoute時、車がroute[0]付近にいる場合、そのWaypointを現在地として占有します。")]
    public bool occupyFirstWaypointOnRouteStart = true;

    [Tooltip("route[0]を現在地として扱う最大距離です。")]
    public float firstWaypointOccupyDistance = 6f;

    [Header("Traffic Stop Debug")]
    [Tooltip("現在停止している理由です。")]
    public string currentStopReason = "";

    private Waypoint reservedWaypoint;
    private Waypoint occupiedWaypoint;
    private bool isStoppedByReservation;
    private Waypoint waitingWaypoint;

    [Header("Stop State")]
    public bool isStoppedByFrontCar;
    private float frontStopTimer;

    // 発進前の広いBox確認は、走行中の毎フレームではなく、
    // 停止後に再発進するときだけ1回行う。
    private bool needsStartMoveSafetyCheck = true;

    [Header("State")]
    public NPC_CarMoveState moveState = NPC_CarMoveState.Idle;

    public Waypoint OccupiedWaypointForDebug => occupiedWaypoint;
    public Waypoint ReservedWaypointForDebug => reservedWaypoint;

    public Waypoint CurrentTargetWaypointForDebug
    {
        get
        {
            if (moveState == NPC_CarMoveState.DrivingRoute &&
                route != null &&
                currentWaypointIndex >= 0 &&
                currentWaypointIndex < route.Count)
            {
                return route[currentWaypointIndex];
            }

            if (moveState == NPC_CarMoveState.Leaving &&
                exitRoute != null &&
                currentExitWaypointIndex >= 0 &&
                currentExitWaypointIndex < exitRoute.Count)
            {
                return exitRoute[currentExitWaypointIndex];
            }

            if ((moveState == NPC_CarMoveState.WaitingToBackOut ||
                 moveState == NPC_CarMoveState.BackingOut) &&
                targetParkingSlot != null)
            {
                return targetParkingSlot.accessWaypoint;
            }

            return null;
        }
    }

    [Header("Finish Settings")]
    public bool destroyOnFinished = false;

    private Car car;
    private Rigidbody carRigidbody;

    private void Awake()
    {
        car = GetComponent<Car>();
        carRigidbody = GetComponent<Rigidbody>();

        if (useScenarioSpeedEffect && scenarioFactorRuntime == null)
        {
            scenarioFactorRuntime = ScenarioFactorRuntime.Instance;
        }

        InitializePhysicsQueryBuffers();

        // 全車が同じフレームで物理判定しないよう、
        // Instance IDから初回判定時刻を少しずつずらす。
        float phase =
            Mathf.Abs(GetInstanceID() % 1000) /
            1000f;

        nextFrontPhysicsCheckAt =
            Time.time +
            phase *
            Mathf.Max(
                0.02f,
                movingFrontCheckInterval
            );

        nextStartMoveSafetyCheckAt =
            Time.time +
            phase *
            Mathf.Max(
                0.02f,
                startMoveSafetyCheckInterval
            );

        if (disableDebugDrawingAtRuntime &&
            Application.isPlaying)
        {
            drawFrontCheckDebug = false;
            drawBackOutCheckGizmo = false;
            drawStartMoveCheckGizmo = false;
        }

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
        ReleaseWaypointReservation(true);

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
        ReleaseWaypointReservation(false);

        route = newRoute;
        currentWaypointIndex = 0;
        needsStartMoveSafetyCheck = true;
        currentStopReason = "";

        if (route != null && route.Count > 0)
        {
            // route[0] が現在位置に近い場合だけ、現在占有中のWaypointとして登録する。
            // これにより、次の区間へ進むときに 交通フロー管理 を正しく取得できる。
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

        Vector3 targetPosition = targetWaypoint.transform.position;
        targetPosition.y = transform.position.y;

        // 現在のRoadCell内で必要な方向転換を先に完了する。
        // 方向転換だけで次区画へ進入しないため、
        // この時点ではRoadCell/Junction許可を取得しない。
        if (TryHandleTurnInPlaceBeforeMove(targetWaypoint, targetPosition))
        {
            ReleaseUnstartedMovePermitForPhysicalWait();
            currentStopReason = "Waypoint方向転換";
            needsStartMoveSafetyCheck = true;
            return;
        }

        // 重要：
        // RoadCell/Junction許可より前に物理前方確認を行う。
        //
        // 旧順序では、前方車両で発進できない車も先に次区画を青予約し、
        // Junction Ownerを保持していたため、赤＋青で全区画が埋まった。
        if (ShouldHoldStopForFrontCar())
        {
            ReleaseUnstartedMovePermitForPhysicalWait();
            currentStopReason = "前方車両";
            needsStartMoveSafetyCheck = true;
            StopCarCompletely();
            return;
        }

        // 広いBox確認も新しい移動許可の取得前に行う。
        if (needsStartMoveSafetyCheck &&
            ShouldStopForStartMoveSafety())
        {
            ReleaseUnstartedMovePermitForPhysicalWait();
            currentStopReason = "発進前安全確認";
            StopCarCompletely();
            return;
        }

        // 物理的に発進可能な車だけが、次区画・Junction・Cycleを予約する。
        if (!TryReserveMoveToWaypoint(targetWaypoint))
        {
            RefreshNetworkWaitReason();

            StopCarCompletely();
            return;
        }

        needsStartMoveSafetyCheck = false;
        currentStopReason = "";

        MoveToTarget(targetPosition, GetEffectiveMoveSpeed());
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
        ReleaseWaypointReservation(true);

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

        // 通常走行と同様に、物理的に発進できることを先に確認する。
        // バックできない車がAccessWaypoint・Junction・Cycleを予約し、
        // 周囲の通行を不要に止めることを防ぐ。
        if (!CanBackOutSafely())
        {
            if (logBackOutSafetyDebug)
            {
                Debug.Log(
                    $"{name}: AccessWaypoint付近に車がいるため、 " +
                    "予約せず出庫待機します。",
                    this
                );
            }

            return;
        }

        // 物理的に出庫可能な車だけが論理進入許可を取得する。
        if (!TryReserveMoveToWaypoint(accessWaypoint, null))
        {
            if (logBackOutSafetyDebug)
            {
                Debug.Log(
                    $"{name}: AccessWaypointへの進入許可を取得できないため、 " +
                    $"出庫待機します。Waypoint={accessWaypoint.name}",
                    this
                );
            }

            return;
        }

        if (logBackOutSafetyDebug)
        {
            Debug.Log(
                $"{name}: 出庫確認・進入許可ともにOK。バックを開始します。",
                this
            );
        }

        moveState = NPC_CarMoveState.BackingOut;
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
        if (checkWhileBackingOut &&
            TryGetBackOutBlockingCar(
                out NPC_CarController blockingCar,
                true))
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

            // exitRouteの先頭にAccessWaypoint自身が含まれている場合、
            // AccessWaypoint → 同じAccessWaypoint の移動申請にならないよう読み飛ばす。
            SkipAccessWaypointAtExitRouteStart(backOutWaypoint);

            if (exitRoute == null || exitRoute.Count == 0 ||
                currentExitWaypointIndex >= exitRoute.Count)
            {
                FinishDriving();
            }
            else
            {
                moveState = NPC_CarMoveState.Leaving;
            }
        }
    }


    private bool TryGetBackOutBlockingCar(
        out NPC_CarController blockingCar,
        bool backingAlreadyStarted = false)
    {
        blockingCar = null;

        if (!useBackOutSafetyCheck)
        {
            return false;
        }

        if (targetParkingSlot == null ||
            targetParkingSlot.accessWaypoint == null)
        {
            return true;
        }

        InitializePhysicsQueryBuffers();

        Transform checkTransform =
            targetParkingSlot
                .accessWaypoint
                .transform;

        Vector3 checkCenter =
            checkTransform.TransformPoint(
                backOutCheckCenterOffset
            );

        int hitCount =
            Physics.OverlapBoxNonAlloc(
                checkCenter,
                backOutCheckBoxSize * 0.5f,
                physicsOverlapBuffer,
                checkTransform.rotation,
                backOutCheckLayerMask,
                QueryTriggerInteraction.Ignore
            );

        WarnIfPhysicsBufferFull(
            hitCount,
            physicsOverlapBuffer.Length,
            "Back Out OverlapBox"
        );

        for (int index = 0;
             index < hitCount;
             index++)
        {
            Collider hit =
                physicsOverlapBuffer[index];

            ResolveVehicleComponents(
                hit,
                out Car hitCar,
                out NPC_CarController hitController
            );

            if (hitCar == null ||
                hitCar == car)
            {
                continue;
            }

            if (ignoreParkedCarsInBackOutCheck)
            {
                if (hitController != null &&
                    (hitController.moveState ==
                        NPC_CarMoveState.Parked ||
                     hitController.moveState ==
                        NPC_CarMoveState.Finished))
                {
                    continue;
                }

                if (hitController == null &&
                    (hitCar.isParked ||
                     hitCar.currentParkingSlot != null))
                {
                    continue;
                }
            }

            if (hitController != null)
            {
                NPC_CarMoveState otherState =
                    hitController.moveState;

                if (!backingAlreadyStarted &&
                    otherState ==
                        NPC_CarMoveState.WaitingToBackOut &&
                    deterministicBackOutPriority)
                {
                    bool thisHasPriority =
                        GetInstanceID() <
                        hitController.GetInstanceID();

                    if (thisHasPriority)
                    {
                        continue;
                    }

                    blockingCar = hitController;
                    return true;
                }

                if (backingAlreadyStarted &&
                    committedBackOutHasPriority)
                {
                    if (otherState ==
                        NPC_CarMoveState.BackingOut)
                    {
                        if (deterministicBackOutPriority)
                        {
                            bool thisHasPriority =
                                GetInstanceID() <
                                hitController.GetInstanceID();

                            if (thisHasPriority)
                            {
                                continue;
                            }
                        }

                        blockingCar = hitController;
                        return true;
                    }

                    // 出庫開始後はAccessWaypointまで進み切る。
                    // 通常走行車・駐車動作中車はBackingOut車を
                    // 前方障害物として検知し、そちらが停止する。
                    continue;
                }

                blockingCar = hitController;
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

        // 出庫直後などに、現在いるWaypointがexitRoute側にも含まれている場合は
        // 同じWaypointへの移動申請をせず次へ進める。
        if (targetWaypoint == occupiedWaypoint)
        {
            currentExitWaypointIndex++;
            return;
        }

        Vector3 targetPosition = targetWaypoint.transform.position;
        targetPosition.y = transform.position.y;

        if (TryHandleTurnInPlaceBeforeMove(targetWaypoint, targetPosition))
        {
            ReleaseUnstartedMovePermitForPhysicalWait();
            currentStopReason = "Waypoint方向転換";
            needsStartMoveSafetyCheck = true;
            return;
        }

        // 出庫後の通常走行でも、物理的に発進できることを確認してから
        // RoadCell/Junction許可を取得する。
        if (ShouldHoldStopForFrontCar())
        {
            ReleaseUnstartedMovePermitForPhysicalWait();
            currentStopReason = "前方車両";
            needsStartMoveSafetyCheck = true;
            StopCarCompletely();
            return;
        }

        if (needsStartMoveSafetyCheck &&
            ShouldStopForStartMoveSafety())
        {
            ReleaseUnstartedMovePermitForPhysicalWait();
            currentStopReason = "発進前安全確認";
            StopCarCompletely();
            return;
        }

        if (!TryReserveMoveToWaypoint(targetWaypoint))
        {
            RefreshNetworkWaitReason();

            StopCarCompletely();
            return;
        }

        needsStartMoveSafetyCheck = false;
        currentStopReason = "";

        MoveToTarget(targetPosition, GetEffectiveMoveSpeed());
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

    private void SkipAccessWaypointAtExitRouteStart(Waypoint accessWaypoint)
    {
        if (exitRoute == null)
        {
            return;
        }

        while (currentExitWaypointIndex < exitRoute.Count)
        {
            Waypoint waypoint = exitRoute[currentExitWaypointIndex];

            if (waypoint == null)
            {
                currentExitWaypointIndex++;
                continue;
            }

            if (waypoint == accessWaypoint)
            {
                currentExitWaypointIndex++;
                continue;
            }

            break;
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
    private bool EnsureMergeTrafficCoordinator()
    {
        if (mergeTrafficCoordinator != null)
        {
            return true;
        }

#if UNITY_2023_1_OR_NEWER
        mergeTrafficCoordinator =
            FindFirstObjectByType<
                MergeTrafficCoordinator>();
#else
        mergeTrafficCoordinator =
            FindObjectOfType<
                MergeTrafficCoordinator>();
#endif

        return
            mergeTrafficCoordinator != null;
    }

    private bool TryReserveWaypointDirect(
        Waypoint waypoint)
    {
        if (waypoint == null)
        {
            return false;
        }

        if (reservedWaypoint == waypoint ||
            occupiedWaypoint == waypoint)
        {
            return true;
        }

        if (!waypoint.TryReserve(this))
        {
            return false;
        }

        if (reservedWaypoint != null &&
            reservedWaypoint !=
                occupiedWaypoint)
        {
            reservedWaypoint
                .ReleaseReservation(this);
        }

        reservedWaypoint = waypoint;
        return true;
    }

    private bool TryReserveMoveToWaypoint(
        Waypoint targetWaypoint)
    {
        return TryReserveMoveToWaypoint(
            targetWaypoint,
            occupiedWaypoint
        );
    }

    private bool TryReserveMoveToWaypoint(
        Waypoint targetWaypoint,
        Waypoint fromWaypoint)
    {
        if (!useWaypointReservation)
        {
            reservedWaypoint =
                targetWaypoint;

            ClearReservationWait();

            return targetWaypoint != null;
        }

        if (targetWaypoint == null)
        {
            SetReservationWait(null);
            return false;
        }

        bool reserved;

        if (EnsureMergeTrafficCoordinator())
        {
            reserved =
                mergeTrafficCoordinator
                    .TryReserveMove(
                        this,
                        fromWaypoint,
                        targetWaypoint
                    );
        }
        else
        {
            reserved =
                TryReserveWaypointDirect(
                    targetWaypoint
                );
        }

        if (!reserved)
        {
            SetReservationWait(
                targetWaypoint
            );

            return false;
        }

        reservedWaypoint =
            targetWaypoint;

        ClearReservationWait();

        if (logWaypointReservationDebug)
        {
            Debug.Log(
                $"{name}: 移動予約成功。"
                + $" From="
                + $"{(fromWaypoint != null ? fromWaypoint.name : "ParkingSlot/Spawn")},"
                + $" Target={targetWaypoint.name}",
                this
            );
        }

        return true;
    }

    private void RefreshNetworkWaitReason()
    {
        if (!isStoppedByReservation)
        {
            currentStopReason = "";
            return;
        }

        if (mergeTrafficCoordinator != null)
        {
            currentStopReason =
                mergeTrafficCoordinator
                    .GetWaitReason(this);

            return;
        }

        string waypointName =
            waitingWaypoint != null
                ? waitingWaypoint.name
                : "null";

        NPC_CarController blocker =
            waitingWaypoint != null
                ? waitingWaypoint.BlockingCar
                : null;

        currentStopReason =
            $"Waypoint予約待機:"
            + $" Target={waypointName},"
            + $" BlockingCar="
            + $"{(blocker != null ? blocker.name : "none")}";
    }

    private void ReleaseUnstartedMovePermitForPhysicalWait()
    {
        // 前方車両や方向転換による一時停止では、
        // 取得済みの移動許可とWaypoint予約を保持します。
        //
        // 許可後の車が入口待機車へ権利を譲り返し、
        // 同時発進することを防ぎます。
    }

    private void ReleasePendingNetworkMove()
    {
        if (mergeTrafficCoordinator != null)
        {
            mergeTrafficCoordinator
                .ReleasePendingMove(this);
        }

        if (reservedWaypoint != null &&
            reservedWaypoint !=
                occupiedWaypoint)
        {
            reservedWaypoint
                .ReleaseReservation(this);

            reservedWaypoint = null;
        }

        ClearReservationWait();
    }

    private void SetReservationWait(
        Waypoint waypoint)
    {
        isStoppedByReservation = true;
        waitingWaypoint = waypoint;

        RefreshNetworkWaitReason();

        if (logWaypointReservationDebug)
        {
            Debug.Log(
                $"{name}: {currentStopReason}",
                this
            );
        }
    }

    private void ClearReservationWait()
    {
        isStoppedByReservation = false;
        waitingWaypoint = null;

        if (currentStopReason.StartsWith(
                "Waypoint予約待機") ||
            currentStopReason.StartsWith(
                "合流待機") ||
            currentStopReason.StartsWith(
                "合流設定待機"))
        {
            currentStopReason = "";
        }
    }

    public bool IsWaitingForWaypoint(
        Waypoint waypoint)
    {
        return
            waypoint != null &&
            isStoppedByReservation &&
            waitingWaypoint == waypoint;
    }

    public bool IsStoppedByReservation()
    {
        return isStoppedByReservation;
    }

    private void ArriveAtWaypoint(
        Waypoint waypoint,
        Waypoint nextWaypoint)
    {
        if (waypoint == null)
        {
            return;
        }

        Waypoint previousWaypoint =
            occupiedWaypoint;

        if (useWaypointReservation &&
            !waypoint.TryEnter(this))
        {
            currentStopReason =
                $"Waypoint到着確定失敗: "
                + waypoint.name;

            Debug.LogError(
                $"{name}: Waypoint到着確定失敗。"
                + $" Waypoint={waypoint.name},"
                + $" ReservedBy="
                + $"{(waypoint.reservedBy != null ? waypoint.reservedBy.name : "null")},"
                + $" OccupiedBy="
                + $"{(waypoint.occupiedBy != null ? waypoint.occupiedBy.name : "null")}",
                waypoint
            );

            StopCarCompletely();
            return;
        }

        if (useWaypointReservation &&
            previousWaypoint != null &&
            previousWaypoint != waypoint)
        {
            previousWaypoint
                .ReleaseOccupancy(this);
        }

        occupiedWaypoint = waypoint;

        if (reservedWaypoint == waypoint)
        {
            reservedWaypoint = null;
        }

        if (mergeTrafficCoordinator != null)
        {
            mergeTrafficCoordinator
                .NotifyArrived(
                    this,
                    previousWaypoint,
                    waypoint
                );
        }

        ClearReservationWait();

        if (logWaypointReservationDebug)
        {
            Debug.Log(
                $"{name}: Waypoint到着。"
                + $" Waypoint={waypoint.name}",
                this
            );
        }
    }

    private void ReleaseWaypointReservation(
        bool leaveNetwork = false)
    {
        if (mergeTrafficCoordinator != null)
        {
            mergeTrafficCoordinator
                .LeaveNetwork(this);
        }

        if (reservedWaypoint != null)
        {
            reservedWaypoint
                .ReleaseReservation(this);

            reservedWaypoint = null;
        }

        if (occupiedWaypoint != null)
        {
            occupiedWaypoint
                .ReleaseOccupancy(this);

            occupiedWaypoint = null;
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

    private void InitializePhysicsQueryBuffers()
    {
        int bufferSize =
            Mathf.Clamp(
                physicsQueryBufferSize,
                8,
                128
            );

        if (physicsOverlapBuffer == null ||
            physicsOverlapBuffer.Length != bufferSize)
        {
            physicsOverlapBuffer =
                new Collider[bufferSize];
        }

        if (physicsCastBuffer == null ||
            physicsCastBuffer.Length != bufferSize)
        {
            physicsCastBuffer =
                new RaycastHit[bufferSize];
        }
    }

    private void WarnIfPhysicsBufferFull(
        int hitCount,
        int bufferLength,
        string queryName)
    {
        if (hitCount < bufferLength ||
            physicsBufferOverflowWarned)
        {
            return;
        }

        physicsBufferOverflowWarned = true;

        Debug.LogWarning(
            $"{name}: {queryName}のNonAllocバッファが満杯です。 " +
            $"Physics Query Buffer Sizeを増やしてください。 " +
            $"Current={bufferLength}",
            this
        );
    }

    private void ResolveVehicleComponents(
        Collider candidate,
        out Car resolvedCar,
        out NPC_CarController resolvedController)
    {
        resolvedCar = null;
        resolvedController = null;

        if (candidate == null)
        {
            return;
        }

        // 車体Colliderは通常Rigidbodyの子なので、
        // attachedRigidbodyから取得すると親階層探索を減らせる。
        Rigidbody attached =
            candidate.attachedRigidbody;

        if (attached != null)
        {
            resolvedCar =
                attached.GetComponent<Car>();

            resolvedController =
                attached.GetComponent<
                    NPC_CarController
                >();
        }

        if (resolvedCar == null)
        {
            resolvedCar =
                candidate.GetComponentInParent<Car>();
        }

        if (resolvedController == null)
        {
            resolvedController =
                candidate.GetComponentInParent<
                    NPC_CarController
                >();
        }
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
        if (!useFrontVehicleDetection ||
            frontSensor == null)
        {
            cachedFrontPhysicsBlocked = false;
            isStoppedByFrontCar = false;
            return false;
        }

        if (usePhysicsQueryThrottling &&
            Time.time < nextFrontPhysicsCheckAt)
        {
            isStoppedByFrontCar =
                cachedFrontPhysicsBlocked;

            return cachedFrontPhysicsBlocked;
        }

        InitializePhysicsQueryBuffers();

        float nextInterval =
            cachedFrontPhysicsBlocked
                ? stoppedFrontCheckInterval
                : movingFrontCheckInterval;

        nextFrontPhysicsCheckAt =
            Time.time +
            Mathf.Max(
                0.02f,
                nextInterval
            );

        Vector3 origin = frontSensor.position;

        Vector3 direction =
            frontSensor.forward;

        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
        {
            direction = transform.forward;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude <= 0.001f)
        {
            cachedFrontPhysicsBlocked = false;
            isStoppedByFrontCar = false;
            return false;
        }

        direction.Normalize();

        if (drawFrontCheckDebug)
        {
            Debug.DrawRay(
                origin,
                direction * frontCheckDistance,
                Color.red,
                Mathf.Max(
                    0.02f,
                    movingFrontCheckInterval
                )
            );
        }

        int overlapCount =
            Physics.OverlapSphereNonAlloc(
                origin,
                frontCheckRadius,
                physicsOverlapBuffer,
                carLayerMask,
                QueryTriggerInteraction.Ignore
            );

        WarnIfPhysicsBufferFull(
            overlapCount,
            physicsOverlapBuffer.Length,
            "Front OverlapSphere"
        );

        for (int index = 0;
             index < overlapCount;
             index++)
        {
            Collider overlap =
                physicsOverlapBuffer[index];

            if (!TryGetPhysicalFrontCar(
                    overlap,
                    out Car hitCar,
                    out NPC_CarController hitController))
            {
                continue;
            }

            if (logFrontVehicleDetection)
            {
                Debug.Log(
                    $"{name}: FrontSensor始点で前方車両を検知しました " +
                    $"→ {hitCar.carId}",
                    this
                );
            }

            cachedFrontPhysicsBlocked = true;
            isStoppedByFrontCar = true;
            return true;
        }

        int castCount =
            Physics.SphereCastNonAlloc(
                origin,
                frontCheckRadius,
                direction,
                physicsCastBuffer,
                frontCheckDistance,
                carLayerMask,
                QueryTriggerInteraction.Ignore
            );

        WarnIfPhysicsBufferFull(
            castCount,
            physicsCastBuffer.Length,
            "Front SphereCast"
        );

        for (int index = 0;
             index < castCount;
             index++)
        {
            RaycastHit hit =
                physicsCastBuffer[index];

            if (!TryGetPhysicalFrontCar(
                    hit.collider,
                    out Car hitCar,
                    out NPC_CarController hitController))
            {
                continue;
            }

            if (logFrontVehicleDetection)
            {
                Debug.Log(
                    $"{name}: 物理前方範囲で車両を検知しました " +
                    $"→ {hitCar.carId}, Distance={hit.distance:F2}",
                    this
                );
            }

            cachedFrontPhysicsBlocked = true;
            isStoppedByFrontCar = true;
            return true;
        }

        cachedFrontPhysicsBlocked = false;
        isStoppedByFrontCar = false;
        return false;
    }

    private bool TryGetPhysicalFrontCar(
        Collider candidate,
        out Car hitCar,
        out NPC_CarController hitController)
    {
        hitCar = null;
        hitController = null;

        if (candidate == null)
        {
            return false;
        }

        ResolveVehicleComponents(
            candidate,
            out hitCar,
            out hitController
        );

        if (hitCar == null || hitCar == car)
        {
            return false;
        }

        if (hitController != null &&
            ShouldIgnoreCarForFrontDetection(
                hitController))
        {
            hitCar = null;
            hitController = null;
            return false;
        }

        // NPC_CarControllerを持たない車両でも、
        // Car側で完全駐車状態が確認できる場合は除外する。
        if (IsFullyParkedVehicle(
                hitCar,
                hitController))
        {
            if (logIgnoredParkedCars)
            {
                Debug.Log(
                    $"{name}: 完全駐車中の車を前方検知から除外しました。 " +
                    $"Other={hitCar.carId}",
                    this
                );
            }

            hitCar = null;
            hitController = null;
            return false;
        }

        // SphereCastの範囲へColliderが入っただけでは、
        // 同一路線の先行車とは限らない。
        //
        // 曲がり角では、直交方向・反対向きレーンの車体側面が
        // FrontSensorへ入り、4台が互いを「前方車両」と判定する
        // 循環停止が発生する。
        //
        // NPC車については、実際の進行方向と横方向距離を確認し、
        // 同一路線の先行車だけを通常の前方障害物として扱う。
        // 交差方向の通行順はRoadCell/Junctionが担当する。
        if (applyLaneFilterToPhysicalFrontDetection &&
            hitController != null &&
            !IsRelevantTrafficObstacle(
                hitController,
                frontSameLaneHalfWidth,
                0.5f))
        {
            if (logFrontVehicleDetection)
            {
                Vector3 myDirection =
                    GetTrafficTravelDirection();

                Vector3 otherDirection =
                    hitController
                        .GetTrafficTravelDirection();

                float directionDot =
                    Vector3.Dot(
                        myDirection,
                        otherDirection
                    );

                Vector3 relative =
                    hitController.transform.position -
                    transform.position;

                relative.y = 0f;

                Vector3 right =
                    Vector3.Cross(
                        Vector3.up,
                        myDirection
                    );

                float lateralDistance =
                    Mathf.Abs(
                        Vector3.Dot(
                            relative,
                            right
                        )
                    );

                Debug.Log(
                    $"{name}: 直交・別レーン車を前方検知から除外。 " +
                    $"Other={hitController.name}#" +
                    $"{hitController.GetInstanceID()}, " +
                    $"DirectionDot={directionDot:F2}, " +
                    $"Lateral={lateralDistance:F2}",
                    this
                );
            }

            hitCar = null;
            hitController = null;
            return false;
        }

        return true;
    }

    private bool ShouldStopForStartMoveSafety()
    {
        if (!useStartMoveSafetyCheck)
        {
            cachedStartMoveSafetyBlocked = false;
            return false;
        }

        if (usePhysicsQueryThrottling &&
            Time.time <
                nextStartMoveSafetyCheckAt)
        {
            return cachedStartMoveSafetyBlocked;
        }

        InitializePhysicsQueryBuffers();

        nextStartMoveSafetyCheckAt =
            Time.time +
            Mathf.Max(
                0.02f,
                startMoveSafetyCheckInterval
            );

        Vector3 checkCenter =
            transform.TransformPoint(
                startMoveCheckCenterOffset
            );

        int hitCount =
            Physics.OverlapBoxNonAlloc(
                checkCenter,
                startMoveCheckBoxSize * 0.5f,
                physicsOverlapBuffer,
                transform.rotation,
                carLayerMask,
                QueryTriggerInteraction.Ignore
            );

        WarnIfPhysicsBufferFull(
            hitCount,
            physicsOverlapBuffer.Length,
            "Start Move OverlapBox"
        );

        for (int index = 0;
             index < hitCount;
             index++)
        {
            Collider hit =
                physicsOverlapBuffer[index];

            ResolveVehicleComponents(
                hit,
                out Car hitCar,
                out NPC_CarController hitController
            );

            if (hitCar == null ||
                hitCar == car)
            {
                continue;
            }

            if (hitController != null)
            {
                if (ShouldIgnoreCarForFrontDetection(
                        hitController))
                {
                    continue;
                }

                if (!IsRelevantTrafficObstacle(
                        hitController,
                        startMoveSameLaneHalfWidth,
                        startMoveIgnoreBehindDistance))
                {
                    continue;
                }
            }
            else
            {
                Vector3 localPosition =
                    transform.InverseTransformPoint(
                        hitCar.transform.position
                    );

                if (localPosition.z <
                    -Mathf.Abs(
                        startMoveIgnoreBehindDistance))
                {
                    continue;
                }

                if (Mathf.Abs(localPosition.x) >
                    Mathf.Max(
                        0.1f,
                        startMoveSameLaneHalfWidth))
                {
                    continue;
                }
            }

            if (logStartMoveSafetyCheck)
            {
                Debug.Log(
                    $"{name}: 発進前確認で進路上の車を検知したため" +
                    $"停止します。Other={hitCar.carId}",
                    this
                );
            }

            cachedStartMoveSafetyBlocked = true;
            isStoppedByFrontCar = true;
            return true;
        }

        cachedStartMoveSafetyBlocked = false;
        return false;
    }

    private Vector3 GetTrafficTravelDirection()
    {
        Waypoint targetWaypoint = null;

        if (reservedWaypoint != null)
        {
            targetWaypoint = reservedWaypoint;
        }
        else if (moveState == NPC_CarMoveState.DrivingRoute &&
                 route != null &&
                 currentWaypointIndex >= 0 &&
                 currentWaypointIndex < route.Count)
        {
            targetWaypoint = route[currentWaypointIndex];
        }
        else if (moveState == NPC_CarMoveState.Leaving &&
                 exitRoute != null &&
                 currentExitWaypointIndex >= 0 &&
                 currentExitWaypointIndex < exitRoute.Count)
        {
            targetWaypoint = exitRoute[currentExitWaypointIndex];
        }

        if (targetWaypoint != null)
        {
            Vector3 routeDirection =
                targetWaypoint.transform.position -
                transform.position;

            routeDirection.y = 0f;

            if (routeDirection.sqrMagnitude > 0.01f)
            {
                return routeDirection.normalized;
            }
        }

        Vector3 fallback = transform.forward;
        fallback.y = 0f;

        if (fallback.sqrMagnitude <= 0.001f)
        {
            return Vector3.forward;
        }

        return fallback.normalized;
    }

    private bool IsRelevantTrafficObstacle(
        NPC_CarController other,
        float sameLaneHalfWidth,
        float allowedBehindDistance)
    {
        if (other == null)
        {
            return false;
        }

        Vector3 myDirection = GetTrafficTravelDirection();
        Vector3 relative =
            other.transform.position -
            transform.position;

        relative.y = 0f;

        float forwardDistance =
            Vector3.Dot(relative, myDirection);

        if (forwardDistance <
            -Mathf.Abs(allowedBehindDistance))
        {
            return false;
        }

        Vector3 right =
            Vector3.Cross(Vector3.up, myDirection);

        float lateralDistance =
            Mathf.Abs(Vector3.Dot(relative, right));

        if (lateralDistance >
            Mathf.Max(0.1f, sameLaneHalfWidth))
        {
            return false;
        }

        // 駐車・出庫中の車は向きに関係なく実障害物として扱う。
        if (other.moveState ==
                NPC_CarMoveState.WaitingToBackOut &&
            ignoreWaitingToBackOutCarsInFrontDetection)
        {
            return false;
        }

        if (other.moveState == NPC_CarMoveState.Parking ||
            other.moveState == NPC_CarMoveState.BackingOut)
        {
            return true;
        }

        if (!useSameLaneDirectionFilter)
        {
            return true;
        }

        Vector3 otherDirection =
            other.GetTrafficTravelDirection();

        float directionDot =
            Vector3.Dot(myDirection, otherDirection);

        // 逆向きレーンや直交レーン上の車は、
        // Waypoint予約側で管理するため前方障害物にしない。
        return directionDot >= sameDirectionDotThreshold;
    }

    private bool ShouldIgnoreCarForFrontDetection(
        NPC_CarController otherCarController)
    {
        if (otherCarController == null)
        {
            return false;
        }

        if (otherCarController.moveState ==
                NPC_CarMoveState.Finished)
        {
            return true;
        }

        if (ignoreWaitingToBackOutCarsInFrontDetection &&
            otherCarController.moveState ==
                NPC_CarMoveState.WaitingToBackOut)
        {
            return true;
        }

        Car otherCar =
            otherCarController.GetComponent<Car>();

        if (IsFullyParkedVehicle(
                otherCar,
                otherCarController))
        {
            if (logIgnoredParkedCars)
            {
                Debug.Log(
                    $"{name}: 完全駐車中のNPC車を検知対象外にしました。 " +
                    $"Other={otherCarController.name}#" +
                    $"{otherCarController.GetInstanceID()}, " +
                    $"Slot={(otherCar != null && otherCar.currentParkingSlot != null ? otherCar.currentParkingSlot.name : "none")}",
                    this
                );
            }

            return true;
        }

        if (mergeTrafficCoordinator != null &&
            mergeTrafficCoordinator
                .ShouldIgnoreForFrontDetection(
                    this,
                    otherCarController,
                    occupiedWaypoint,
                    reservedWaypoint))
        {
            return true;
        }

        return false;
    }

    private bool IsFullyParkedVehicle(
        Car otherCar,
        NPC_CarController otherController)
    {
        if (!ignoreFullyParkedCarsInFrontDetection)
        {
            return false;
        }

        if (otherController != null)
        {
            // Parkedだけを除外する。
            //
            // Parking:
            //   駐車位置へ移動中なので実障害物。
            //
            // WaitingToBackOut / BackingOut / Leaving:
            //   通路へ進入する可能性があるため実障害物。
            if (otherController.moveState ==
                    NPC_CarMoveState.Parked)
            {
                return true;
            }

            return false;
        }

        // Controllerを持たない車両はCarの状態を使用する。
        // currentParkingSlotだけでは出庫中にも残る可能性があるため、
        // isParkedとの両方が成立する場合だけ完全駐車とみなす。
        return otherCar != null &&
               otherCar.isParked &&
               otherCar.currentParkingSlot != null;
    }

    private float GetEffectiveMoveSpeed()
    {
        if (!useScenarioSpeedEffect)
        {
            return moveSpeed;
        }

        if (scenarioFactorRuntime == null)
        {
            scenarioFactorRuntime = ScenarioFactorRuntime.Instance;
        }

        return scenarioFactorRuntime != null
            ? scenarioFactorRuntime.GetEffectiveVehicleSpeed(moveSpeed)
            : moveSpeed;
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
        needsStartMoveSafetyCheck = true;

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
        ReleaseWaypointReservation(true);

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

