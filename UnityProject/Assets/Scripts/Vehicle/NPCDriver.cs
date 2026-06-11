using System.Collections.Generic;
using UnityEngine;

public enum NPCDrivingState
{
    Idle,
    SeekingSlot,
    DrivingToSlot,
    Parking,
    Parked,
    Blocked
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

    [Header("Behavior")]
    public bool startOnPlay = false;
    public bool autoCollectCandidateSlots = true;
    public bool useAssignedSlotOnly = false;

    [Header("Debug")]
    public NPCDrivingState state = NPCDrivingState.Idle;
    public bool logStateChanges = true;

    private PathFollower pathFollower;
    private ParkingAction parkingAction;
    private ParkingSlot targetSlot;

    private void Awake()
    {
        pathFollower = GetComponent<PathFollower>();
        parkingAction = GetComponent<ParkingAction>();

        if (string.IsNullOrWhiteSpace(npcId))
        {
            Car car = GetComponent<Car>();
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
        }

        if (parkingAction != null)
        {
            parkingAction.ParkingCompleted += HandleParkingCompleted;
        }
    }

    private void OnDisable()
    {
        if (pathFollower != null)
        {
            pathFollower.PathCompleted -= HandlePathCompleted;
        }

        if (parkingAction != null)
        {
            parkingAction.ParkingCompleted -= HandleParkingCompleted;
        }
    }

    private void Start()
    {
        if (startOnPlay)
        {
            StartDriving();
        }
    }

    [ContextMenu("Start Driving")]
    public void StartDriving()
    {
        if (autoCollectCandidateSlots)
        {
            AutoCollectCandidateSlots();
        }

        SetState(NPCDrivingState.SeekingSlot);
        targetSlot = SelectTargetSlot();

        if (targetSlot == null)
        {
            Debug.LogWarning($"{name}: No available parking slot found.");
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
        if (assignedSlot != null && assignedSlot.IsAvailable())
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

            Transform approachPoint = slot.GetApproachPoint();
            float distance = Vector3.SqrMagnitude(transform.position - approachPoint.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestSlot = slot;
            }
        }

        return nearestSlot;
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
        if (state != NPCDrivingState.DrivingToSlot || targetSlot == null)
        {
            return;
        }

        SetState(NPCDrivingState.Parking);
        parkingAction.StartParking(targetSlot);
    }

    private void HandleParkingCompleted(ParkingSlot slot)
    {
        if (slot != targetSlot)
        {
            return;
        }

        SetState(NPCDrivingState.Parked);
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
    }
}
