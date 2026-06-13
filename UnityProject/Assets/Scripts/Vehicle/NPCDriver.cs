using System.Collections.Generic;
using UnityEngine;

public enum NPCDrivingState
{
    Idle,
    SeekingSlot,
    DrivingToSlot,
    Parking,
    Parked,
    Blocked,
    WaitingToExit,
    DrivingToExit,
    Exited
}

[RequireComponent(typeof(PathFollower))]
[RequireComponent(typeof(ParkingAction))]
public class NPCDriver : MonoBehaviour
{
    [Header("Identity")]
    public string npcId;

    [Header("References")]
    public RoutePlanner routePlanner;
    public Transform parkingSlotsRoot;
    public ParkingSlot assignedSlot;
    public List<ParkingSlot> candidateSlots = new List<ParkingSlot>();
    public RoadNode exitNode;

    [Header("Behavior")]
    public bool startOnPlay = false;
    public bool autoCollectCandidateSlots = true;
    public bool useAssignedSlotOnly = false;
    public bool usePhase2SafetyChecks = true;
    public bool requireDrivableAreaForManeuver = true;
    public bool validateExitRouteAreas = true;
    public bool requireLaneDrivableAreaForExit = true;
    public bool autoExitAfterParking = false;
    public bool deactivateOnExit = false;
    public float exitWaitSeconds = 8f;
    public float blockedTimeout = 5f;

    [Header("Debug")]
    public NPCDrivingState state = NPCDrivingState.Idle;
    public bool logStateChanges = true;
    public ParkingSlot targetSlot;
    public ParkingManeuverType selectedManeuver = ParkingManeuverType.FrontIn;
    public ManeuverPath selectedManeuverPath;
    public List<Vector3> assignedRoute = new List<Vector3>();
    public float debugExitWaitTimer;
    public string debugLastBlockedReason;

    private PathFollower pathFollower;
    private ParkingAction parkingAction;
    private VehicleCollisionShape collisionShape;
    private Car car;
    private float exitWaitTimer;
    private bool exitSequenceStarted;
    private readonly List<DrivableArea> drivableAreas = new List<DrivableArea>();
    private readonly List<NoDriveArea> noDriveAreas = new List<NoDriveArea>();

    private void Awake()
    {
        pathFollower = GetComponent<PathFollower>();
        parkingAction = GetComponent<ParkingAction>();
        collisionShape = GetComponent<VehicleCollisionShape>();
        car = GetComponent<Car>();
        SyncVehicleCollisionFromCar();

        if (string.IsNullOrWhiteSpace(npcId))
        {
            npcId = car != null ? car.carId : gameObject.name;
        }
    }

    private void OnEnable()
    {
        if (pathFollower == null)
        {
            pathFollower = GetComponent<PathFollower>();
        }

        if (parkingAction == null)
        {
            parkingAction = GetComponent<ParkingAction>();
        }

        if (pathFollower != null)
        {
            pathFollower.PathCompleted += HandlePathCompleted;
            pathFollower.PathBlocked += HandlePathBlocked;
        }

        if (parkingAction != null)
        {
            parkingAction.ParkingCompleted += HandleParkingCompleted;
            parkingAction.ParkingFailed += HandleParkingFailed;
        }
    }

    private void OnDisable()
    {
        if (pathFollower != null)
        {
            pathFollower.PathCompleted -= HandlePathCompleted;
            pathFollower.PathBlocked -= HandlePathBlocked;
        }

        if (parkingAction != null)
        {
            parkingAction.ParkingCompleted -= HandleParkingCompleted;
            parkingAction.ParkingFailed -= HandleParkingFailed;
        }
    }

    private void Start()
    {
        if (startOnPlay)
        {
            StartDriving();
        }
    }

    private void Update()
    {
        if (state == NPCDrivingState.Parked && autoExitAfterParking && !exitSequenceStarted)
        {
            StartExitWait();
            return;
        }

        if (state != NPCDrivingState.WaitingToExit)
        {
            return;
        }

        exitWaitTimer -= Time.deltaTime;
        debugExitWaitTimer = Mathf.Max(0f, exitWaitTimer);

        if (exitWaitTimer <= 0f)
        {
            StartExitRoute();
        }
    }

    private void SyncVehicleCollisionFromCar()
    {
        if (car == null || collisionShape == null)
        {
            return;
        }

        float length = Mathf.Max(0.01f, car.length);
        float width = Mathf.Max(0.01f, car.width);
        collisionShape.bodyLength = length;
        collisionShape.bodyWidth = width;

        if (length >= 9f || width >= 4f)
        {
            collisionShape.bodyHeight = Mathf.Max(collisionShape.bodyHeight, 3f);
        }

        BoxCollider boxCollider = GetComponent<BoxCollider>();
        if (boxCollider != null)
        {
            boxCollider.size = new Vector3(width, collisionShape.bodyHeight, length);
            boxCollider.center = new Vector3(0f, collisionShape.bodyHeight * 0.5f, 0f);
        }
    }

    [ContextMenu("Start Driving")]
    public void StartDriving()
    {
        debugLastBlockedReason = string.Empty;
        exitSequenceStarted = false;
        exitWaitTimer = 0f;
        debugExitWaitTimer = 0f;

        if (autoCollectCandidateSlots)
        {
            AutoCollectCandidateSlots();
        }

        CollectDrivableAreas();
        CollectNoDriveAreas();
        SetState(NPCDrivingState.SeekingSlot);
        targetSlot = SelectTargetSlot();

        if (targetSlot == null)
        {
            Debug.LogWarning($"{name}: No available parking slot found.");
            SetState(NPCDrivingState.Blocked);
            return;
        }

        float selectedManeuverScore;
        if (!TrySelectManeuver(targetSlot, out selectedManeuver, out selectedManeuverPath, out selectedManeuverScore))
        {
            Debug.LogWarning($"{name}: No safe parking maneuver found for {targetSlot.slotId}.");
            SetState(NPCDrivingState.Blocked);
            return;
        }

        if (!targetSlot.TryReserve())
        {
            Debug.LogWarning($"{name}: Failed to reserve parking slot {targetSlot.slotId}.");
            SetState(NPCDrivingState.Blocked);
            return;
        }

        List<Vector3> route = BuildRoute(targetSlot);
        if (route.Count == 0)
        {
            Debug.LogWarning($"{name}: Route is empty for parking slot {targetSlot.slotId}.");
            targetSlot.SetEmpty();
            SetState(NPCDrivingState.Blocked);
            return;
        }

        assignedRoute.Clear();
        assignedRoute.AddRange(route);
        SetState(NPCDrivingState.DrivingToSlot);
        pathFollower.SetPath(route);
    }

    [ContextMenu("Auto Collect Candidate Slots")]
    public void AutoCollectCandidateSlots()
    {
        candidateSlots.Clear();

        ParkingSlot[] slots;
        if (parkingSlotsRoot != null)
        {
            slots = parkingSlotsRoot.GetComponentsInChildren<ParkingSlot>();
        }
        else
        {
            slots = FindObjectsOfType<ParkingSlot>();
        }

        foreach (ParkingSlot slot in slots)
        {
            if (slot != null)
            {
                candidateSlots.Add(slot);
            }
        }
    }

    private ParkingSlot SelectTargetSlot()
    {
        if (assignedSlot != null && assignedSlot.IsAvailable() && IsSlotSelectable(assignedSlot))
        {
            return assignedSlot;
        }

        if (useAssignedSlotOnly)
        {
            return null;
        }

        ParkingSlot nearestSlot = null;
        float nearestDistance = float.MaxValue;

        foreach (ParkingSlot slot in candidateSlots)
        {
            if (slot == null || !slot.IsAvailable())
            {
                continue;
            }

            if (!IsSlotSelectable(slot))
            {
                continue;
            }

            Transform approachPoint = slot.GetApproachPoint();
            float distance = Vector3.SqrMagnitude(transform.position - approachPoint.position);
            ParkingManeuverType candidateManeuverType;
            ManeuverPath candidateManeuverPath;
            float maneuverScore;
            if (TrySelectManeuver(slot, out candidateManeuverType, out candidateManeuverPath, out maneuverScore))
            {
                distance += maneuverScore;
            }

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestSlot = slot;
            }
        }

        return nearestSlot;
    }

    private bool IsSlotSelectable(ParkingSlot slot)
    {
        if (slot == null || !slot.IsAvailable())
        {
            return false;
        }

        if (!usePhase2SafetyChecks)
        {
            return true;
        }

        ParkingManeuverType candidateManeuverType;
        ManeuverPath candidateManeuverPath;
        float candidateScore;
        return TrySelectManeuver(slot, out candidateManeuverType, out candidateManeuverPath, out candidateScore);
    }

    private List<Vector3> BuildRoute(ParkingSlot slot)
    {
        if (routePlanner == null)
        {
            routePlanner = FindObjectOfType<RoutePlanner>();
        }

        if (routePlanner != null)
        {
            return routePlanner.BuildRouteFromPosition(transform.position, slot);
        }

        List<Vector3> route = new List<Vector3>();
        route.Add(slot.GetApproachPoint().position);
        return route;
    }

    private void HandlePathCompleted()
    {
        if (state == NPCDrivingState.DrivingToExit)
        {
            CompleteExit();
            return;
        }

        if (state != NPCDrivingState.DrivingToSlot || targetSlot == null)
        {
            return;
        }

        Vector3 parkingStartPosition = ResolveParkingStartPosition(targetSlot);
        Vector3 toParkingStart = parkingStartPosition - transform.position;
        toParkingStart.y = 0f;
        if (toParkingStart.magnitude > 1.25f)
        {
            List<Vector3> remainingRoute = new List<Vector3>();
            remainingRoute.Add(parkingStartPosition);
            assignedRoute.Clear();
            assignedRoute.AddRange(remainingRoute);
            pathFollower.SetPath(remainingRoute);
            return;
        }

        SetState(NPCDrivingState.Parking);
        parkingAction.StartParking(targetSlot, selectedManeuver, selectedManeuverPath);
    }

    private void HandleParkingCompleted(ParkingSlot slot)
    {
        if (slot != targetSlot)
        {
            return;
        }

        SetState(NPCDrivingState.Parked);
        if (autoExitAfterParking)
        {
            StartExitWait();
        }
    }

    private void HandleParkingFailed(ParkingSlot slot, string reason)
    {
        if (slot != targetSlot)
        {
            return;
        }

        Debug.LogWarning($"{npcId}: Parking failed at {slot.slotId}. {reason}");
        slot.SetEmpty();
        SetState(NPCDrivingState.Blocked);
    }

    private void HandlePathBlocked(float blockedDuration)
    {
        if ((state != NPCDrivingState.DrivingToSlot && state != NPCDrivingState.DrivingToExit) || blockedDuration < blockedTimeout)
        {
            return;
        }

        Debug.LogWarning($"{npcId}: Path blocked for {blockedDuration:0.0}s.");
        pathFollower.Stop();
        if (targetSlot != null && targetSlot.state == ParkingSlotState.Reserved)
        {
            targetSlot.SetEmpty();
        }

        SetState(NPCDrivingState.Blocked);
    }

    private void StartExitWait()
    {
        exitSequenceStarted = true;
        exitWaitTimer = Mathf.Max(0f, exitWaitSeconds);
        debugExitWaitTimer = exitWaitTimer;

        if (exitWaitTimer <= 0f)
        {
            StartExitRoute();
            return;
        }

        SetState(NPCDrivingState.WaitingToExit);
    }

    private void StartExitRoute()
    {
        List<Vector3> route = BuildExitRoute();
        if (route.Count == 0)
        {
            debugLastBlockedReason = "Exit route is empty.";
            Debug.LogWarning($"{npcId}: {debugLastBlockedReason}");
            SetState(NPCDrivingState.Blocked);
            return;
        }

        CollectDrivableAreas();
        CollectNoDriveAreas();
        if (!IsExitRouteSafe(route, out string unsafeReason))
        {
            debugLastBlockedReason = $"Exit route is unsafe. {unsafeReason}";
            Debug.LogWarning($"{npcId}: {debugLastBlockedReason}");
            SetState(NPCDrivingState.Blocked);
            return;
        }

        if (targetSlot != null)
        {
            targetSlot.SetEmpty();
        }

        assignedRoute.Clear();
        assignedRoute.AddRange(route);
        debugLastBlockedReason = string.Empty;
        SetState(NPCDrivingState.DrivingToExit);
        pathFollower.SetPath(route);
    }

    private List<Vector3> BuildExitRoute()
    {
        List<Vector3> route = new List<Vector3>();

        if (routePlanner == null)
        {
            routePlanner = FindObjectOfType<RoutePlanner>();
        }

        if (exitNode == null)
        {
            GameObject exitObject = GameObject.Find("NPC_Demo_ExitGate");
            if (exitObject == null)
            {
                exitObject = GameObject.Find("NPC_Demo_Exit");
            }

            exitNode = exitObject != null ? exitObject.GetComponent<RoadNode>() : null;
        }

        if (targetSlot != null)
        {
            RoadNode slotNode = routePlanner != null ? routePlanner.ResolveSlotNode(targetSlot) : null;
            Vector3 roadExitPoint = ResolveSlotRoadExitPoint(targetSlot, slotNode);
            AddExitPoint(route, BuildSlotExitPoint(targetSlot, roadExitPoint));
            AddExitPoint(route, roadExitPoint);
        }

        if (routePlanner != null && exitNode != null)
        {
            RoadNode slotNode = routePlanner.ResolveSlotNode(targetSlot);
            if (slotNode != null)
            {
                AppendRoute(route, routePlanner.BuildRouteBetweenNodes(slotNode, exitNode));
                return route;
            }

            AppendRoute(route, routePlanner.BuildRouteToNode(GetLastRoutePosition(route, transform.position), exitNode));
            return route;
        }

        if (exitNode != null)
        {
            AddExitPoint(route, exitNode.transform);
        }

        return route;
    }

    private Vector3 ResolveParkingStartPosition(ParkingSlot slot)
    {
        Transform parkingPoint = slot.GetParkingPoint();
        Transform approachPoint = slot.GetApproachPoint();
        Vector3 approachPosition = approachPoint.position;

        Vector3 fromParking = approachPosition - parkingPoint.position;
        fromParking.y = 0f;
        if (fromParking.sqrMagnitude > 0.001f)
        {
            return approachPosition;
        }

        if (routePlanner == null)
        {
            routePlanner = FindObjectOfType<RoutePlanner>();
        }

        RoadNode slotNode = routePlanner != null ? routePlanner.ResolveSlotNode(slot) : null;
        return slotNode != null ? slotNode.Position : approachPosition;
    }

    private Vector3 ResolveSlotRoadExitPoint(ParkingSlot slot, RoadNode slotNode)
    {
        Transform parkingPoint = slot.GetParkingPoint();
        Transform approachPoint = slot.GetApproachPoint();
        Vector3 roadExitPoint = approachPoint.position;
        roadExitPoint.y = parkingPoint.position.y;

        Vector3 fromParkingToApproach = roadExitPoint - parkingPoint.position;
        fromParkingToApproach.y = 0f;
        if (fromParkingToApproach.sqrMagnitude > 0.001f)
        {
            return roadExitPoint;
        }

        if (slotNode != null)
        {
            roadExitPoint = slotNode.Position;
            roadExitPoint.y = parkingPoint.position.y;
        }

        return roadExitPoint;
    }

    private Vector3 BuildSlotExitPoint(ParkingSlot slot, Vector3 roadExitPoint)
    {
        Transform parkingPoint = slot.GetParkingPoint();
        Vector3 fromParkingToApproach = roadExitPoint - parkingPoint.position;
        fromParkingToApproach.y = 0f;

        if (fromParkingToApproach.sqrMagnitude <= 0.001f)
        {
            return parkingPoint.position;
        }

        float exitDistance = Mathf.Min(fromParkingToApproach.magnitude, Mathf.Max(slot.slotDepth * 0.5f, 1f));
        Vector3 exitPoint = parkingPoint.position + fromParkingToApproach.normalized * exitDistance;
        exitPoint.y = parkingPoint.position.y;
        return exitPoint;
    }

    private void AddExitPoint(List<Vector3> route, Transform point)
    {
        if (route == null || point == null)
        {
            return;
        }

        AddExitPoint(route, point.position);
    }

    private void AddExitPoint(List<Vector3> route, Vector3 point)
    {
        if (route == null)
        {
            return;
        }

        if (route.Count > 0 && Vector3.Distance(route[route.Count - 1], point) <= 0.1f)
        {
            return;
        }

        route.Add(point);
    }

    private void AppendRoute(List<Vector3> route, List<Vector3> points)
    {
        if (route == null || points == null)
        {
            return;
        }

        foreach (Vector3 point in points)
        {
            AddExitPoint(route, point);
        }
    }

    private Vector3 GetLastRoutePosition(List<Vector3> route, Vector3 fallback)
    {
        return route != null && route.Count > 0 ? route[route.Count - 1] : fallback;
    }

    private void CompleteExit()
    {
        SetState(NPCDrivingState.Exited);
        debugExitWaitTimer = 0f;

        if (deactivateOnExit)
        {
            gameObject.SetActive(false);
        }
    }

    private bool TrySelectManeuver(ParkingSlot slot, out ParkingManeuverType maneuverType, out ManeuverPath maneuverPath, out float score)
    {
        maneuverType = ParkingManeuverType.FrontIn;
        maneuverPath = null;
        score = float.MaxValue;

        bool found = false;
        ManeuverPath[] paths = slot.GetManeuverPaths();

        if (paths != null && paths.Length > 0)
        {
            foreach (ManeuverPath path in paths)
            {
                if (path == null || !IsPathForSlot(slot, path))
                {
                    continue;
                }

                if (TryScoreManeuver(slot, path.maneuverType, path, out float candidateScore) && candidateScore < score)
                {
                    maneuverType = path.maneuverType;
                    maneuverPath = path;
                    score = candidateScore;
                    found = true;
                }
            }
        }
        else
        {
            if (TryScoreManeuver(slot, ParkingManeuverType.FrontIn, null, out float frontScore))
            {
                maneuverType = ParkingManeuverType.FrontIn;
                score = frontScore;
                found = true;
            }

            if (TryScoreManeuver(slot, ParkingManeuverType.ReverseIn, null, out float reverseScore) && reverseScore < score)
            {
                maneuverType = ParkingManeuverType.ReverseIn;
                score = reverseScore;
                found = true;
            }
        }

        return found;
    }

    private bool TryScoreManeuver(ParkingSlot slot, ParkingManeuverType maneuverType, ManeuverPath maneuverPath, out float score)
    {
        score = float.MaxValue;

        if (!IsManeuverAllowed(slot, maneuverType))
        {
            return false;
        }

        List<Vector3> points = BuildManeuverPoints(slot, maneuverType, maneuverPath);
        if (points.Count == 0)
        {
            return false;
        }

        if (usePhase2SafetyChecks && !IsManeuverSafe(slot, maneuverType, maneuverPath, points))
        {
            return false;
        }

        float routeDistance = Vector3.Distance(transform.position, slot.GetApproachPoint().position);
        float maneuverDistance = EstimateDistance(points);
        float maneuverTime = maneuverPath != null ? maneuverPath.estimatedDuration : maneuverType == ParkingManeuverType.ReverseIn ? 5f : 4f;
        float preferencePenalty = GetPreferencePenalty(slot, maneuverType);
        float exitEaseBonus = maneuverType == ParkingManeuverType.ReverseIn ? -0.5f : 0f;

        score = routeDistance + maneuverDistance + maneuverTime + preferencePenalty + exitEaseBonus;
        return true;
    }

    private bool IsManeuverAllowed(ParkingSlot slot, ParkingManeuverType maneuverType)
    {
        if (maneuverType == ParkingManeuverType.FrontIn && !slot.allowFrontIn)
        {
            return false;
        }

        if (maneuverType == ParkingManeuverType.ReverseIn && !slot.allowReverseIn)
        {
            return false;
        }

        if (slot.preferredManeuver == ParkingManeuverPreference.FrontInOnly && maneuverType != ParkingManeuverType.FrontIn)
        {
            return false;
        }

        if (slot.preferredManeuver == ParkingManeuverPreference.ReverseInOnly && maneuverType != ParkingManeuverType.ReverseIn)
        {
            return false;
        }

        return true;
    }

    private bool IsManeuverSafe(ParkingSlot slot, ParkingManeuverType maneuverType, ManeuverPath maneuverPath, List<Vector3> points)
    {
        if (collisionShape == null)
        {
            collisionShape = GetComponent<VehicleCollisionShape>();
        }

        ParkingSlotGeometry geometry = slot.GetGeometry();
        Transform parkingPoint = slot.GetParkingPoint();

        if (geometry != null && collisionShape != null && !geometry.IsVehiclePoseInside(collisionShape, parkingPoint.position, parkingPoint.rotation))
        {
            return false;
        }

        if (collisionShape != null && HasBlockingOverlap(parkingPoint.position, parkingPoint.rotation))
        {
            return false;
        }

        if (maneuverPath != null && slot.aisleWidth < maneuverPath.requiredClearance)
        {
            return false;
        }

        for (int i = 0; i < points.Count; i++)
        {
            Quaternion poseRotation = EstimateManeuverPoseRotation(slot, points, i);

            if (collisionShape != null && HasBlockingOverlap(points[i], poseRotation))
            {
                return false;
            }

            if (requireDrivableAreaForManeuver && drivableAreas.Count > 0 && !IsVehiclePoseInsideAnyDrivableArea(points[i], poseRotation))
            {
                return false;
            }
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

    private bool IsVehiclePoseInsideAnyDrivableArea(Vector3 point, Quaternion rotation)
    {
        foreach (DrivableArea area in drivableAreas)
        {
            if (area != null && area.ContainsVehiclePose(collisionShape, point, rotation))
            {
                return true;
            }
        }

        return false;
    }

    private Quaternion EstimateManeuverPoseRotation(ParkingSlot slot, List<Vector3> points, int index)
    {
        Transform parkingPoint = slot.GetParkingPoint();
        if (index >= points.Count - 2)
        {
            return parkingPoint.rotation;
        }

        Vector3 direction = points[index + 1] - points[index];
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
        {
            return parkingPoint.rotation;
        }

        return Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private List<Vector3> BuildManeuverPoints(ParkingSlot slot, ParkingManeuverType maneuverType, ManeuverPath maneuverPath)
    {
        if (maneuverPath != null)
        {
            return maneuverPath.BuildWorldPoints(slot);
        }

        List<Vector3> points = new List<Vector3>();
        points.Add(slot.GetApproachPoint().position);

        Transform entryPoint = maneuverType == ParkingManeuverType.ReverseIn ? slot.reverseEntryPoint : slot.frontEntryPoint;
        if (entryPoint != null)
        {
            points.Add(entryPoint.position);
        }

        points.Add(slot.GetParkingPoint().position);
        return points;
    }

    private float EstimateDistance(List<Vector3> points)
    {
        float distance = 0f;
        Vector3 previousPoint = transform.position;

        foreach (Vector3 point in points)
        {
            distance += Vector3.Distance(previousPoint, point);
            previousPoint = point;
        }

        return distance;
    }

    private float GetPreferencePenalty(ParkingSlot slot, ParkingManeuverType maneuverType)
    {
        switch (slot.preferredManeuver)
        {
            case ParkingManeuverPreference.PreferFrontIn:
                return maneuverType == ParkingManeuverType.FrontIn ? -0.5f : 1.5f;
            case ParkingManeuverPreference.PreferReverseIn:
                return maneuverType == ParkingManeuverType.ReverseIn ? -0.5f : 1.5f;
            default:
                return 0f;
        }
    }

    private bool IsPathForSlot(ParkingSlot slot, ManeuverPath path)
    {
        return string.IsNullOrWhiteSpace(path.slotId) || path.slotId == slot.slotId;
    }

    private void CollectDrivableAreas()
    {
        drivableAreas.Clear();
        drivableAreas.AddRange(FindObjectsOfType<DrivableArea>());
    }

    private void CollectNoDriveAreas()
    {
        noDriveAreas.Clear();
        noDriveAreas.AddRange(FindObjectsOfType<NoDriveArea>());
    }

    private bool IsExitRouteSafe(List<Vector3> route, out string reason)
    {
        reason = string.Empty;
        if (!validateExitRouteAreas || route == null || route.Count == 0)
        {
            return true;
        }

        if (collisionShape == null)
        {
            collisionShape = GetComponent<VehicleCollisionShape>();
        }

        // The first two exit segments are the vehicle leaving its own slot and merging into the aisle.
        const int firstValidatedSegmentIndex = 3;
        for (int i = firstValidatedSegmentIndex; i < route.Count; i++)
        {
            Vector3 from = route[i - 1];
            Vector3 to = route[i];
            Vector3 direction = to - from;
            direction.y = 0f;

            if (direction.sqrMagnitude <= 0.001f)
            {
                continue;
            }

            Quaternion rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            float distance = direction.magnitude;
            int sampleCount = Mathf.Max(1, Mathf.CeilToInt(distance / 2f));

            for (int sampleIndex = 0; sampleIndex <= sampleCount; sampleIndex++)
            {
                float t = sampleIndex / (float)sampleCount;
                Vector3 sample = Vector3.Lerp(from, to, t);

                if (requireLaneDrivableAreaForExit && HasRouteDrivableAreas() && !IsVehiclePoseInsideAnyRouteDrivableArea(sample, rotation))
                {
                    reason = $"Sample outside lane drivable area at {sample:F2}.";
                    return false;
                }

                if (IsVehiclePoseInsideAnyNoDriveArea(sample, rotation, out NoDriveArea noDriveArea))
                {
                    string areaId = noDriveArea != null ? noDriveArea.areaId : "unknown";
                    reason = $"Sample intersects no-drive area '{areaId}' at {sample:F2}.";
                    return false;
                }
            }
        }

        return true;
    }

    private bool HasRouteDrivableAreas()
    {
        foreach (DrivableArea area in drivableAreas)
        {
            if (IsRouteDrivableArea(area))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsVehiclePoseInsideAnyRouteDrivableArea(Vector3 point, Quaternion rotation)
    {
        foreach (DrivableArea area in drivableAreas)
        {
            if (IsRouteDrivableArea(area) && area.ContainsVehiclePose(collisionShape, point, rotation))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsRouteDrivableArea(DrivableArea area)
    {
        return area != null
            && area.HasPolygon
            && (area.areaType == DrivableAreaType.Lane
                || area.areaType == DrivableAreaType.Entrance
                || area.areaType == DrivableAreaType.Exit);
    }

    private bool IsVehiclePoseInsideAnyNoDriveArea(Vector3 point, Quaternion rotation, out NoDriveArea hitArea)
    {
        foreach (NoDriveArea area in noDriveAreas)
        {
            if (area != null && area.ContainsVehiclePose(collisionShape, point, rotation))
            {
                hitArea = area;
                return true;
            }
        }

        hitArea = null;
        return false;
    }

    private void SetState(NPCDrivingState nextState)
    {
        if (state == nextState)
        {
            return;
        }

        if (logStateChanges)
        {
            Debug.Log($"{npcId}: {state} -> {nextState}");
        }

        state = nextState;

        if (car == null)
        {
            car = GetComponent<Car>();
        }

        if (car != null)
        {
            car.currentState = nextState;
        }
    }
}
