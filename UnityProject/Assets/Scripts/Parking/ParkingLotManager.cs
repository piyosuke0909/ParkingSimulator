using System.Collections.Generic;
using UnityEngine;

public class ParkingLotManager : MonoBehaviour
{
    [Header("Slots")]
    public List<ParkingSlot> allSlots = new List<ParkingSlot>();

    private Dictionary<string, ParkingSlot> slotDictionary = new Dictionary<string, ParkingSlot>();

    [Header("Physical Slot Check")]
    public bool usePhysicalSlotCheck = true;
    public LayerMask carLayerMask;
    public Vector3 slotCheckBoxSize = new Vector3(10f, 4f, 8f);

    [Header("Debug")]
    public bool logOnStart = true;

    private void Awake()
    {
        RegisterAllSlots();
    }

    private void Start()
    {
        if (logOnStart)
        {
            Debug.Log($"ParkingSlotìoò^äÆóπ: {allSlots.Count}åè");
            Debug.Log($"Empty: {GetSlotCountByState(ParkingSlotState.Empty)} / " +
                      $"Reserved: {GetSlotCountByState(ParkingSlotState.Reserved)} / " +
                      $"Occupied: {GetSlotCountByState(ParkingSlotState.Occupied)} / " +
                      $"Disabled: {GetSlotCountByState(ParkingSlotState.Disabled)}");
        }
    }

    [ContextMenu("Register All Slots")]
    public void RegisterAllSlots()
    {
        allSlots.Clear();
        slotDictionary.Clear();

#if UNITY_2023_1_OR_NEWER
        ParkingSlot[] slots = FindObjectsByType<ParkingSlot>(FindObjectsSortMode.None);
#else
        ParkingSlot[] slots = FindObjectsOfType<ParkingSlot>();
#endif

        foreach (ParkingSlot slot in slots)
        {
            if (slot == null)
            {
                continue;
            }

            allSlots.Add(slot);

            if (string.IsNullOrEmpty(slot.slotId))
            {
                Debug.LogWarning($"Slot IDÇ™ñ¢ê›íËÇ≈Ç∑: {slot.name}");
                continue;
            }

            if (slotDictionary.ContainsKey(slot.slotId))
            {
                Debug.LogWarning($"Slot IDÇ™èdï°ÇµÇƒÇ¢Ç‹Ç∑: {slot.slotId}");
                continue;
            }

            slotDictionary.Add(slot.slotId, slot);
        }
    }

    public ParkingSlot GetSlotById(string slotId)
    {
        if (string.IsNullOrEmpty(slotId))
        {
            return null;
        }

        if (slotDictionary.TryGetValue(slotId, out ParkingSlot slot))
        {
            return slot;
        }

        Debug.LogWarning($"éwíËÇ≥ÇÍÇΩSlot IDÇ™å©Ç¬Ç©ÇËÇ‹ÇπÇÒ: {slotId}");
        return null;
    }

    public List<ParkingSlot> GetSlotsByArea(string areaId)
    {
        List<ParkingSlot> result = new List<ParkingSlot>();

        foreach (ParkingSlot slot in allSlots)
        {
            if (slot == null)
            {
                continue;
            }

            if (slot.areaId == areaId)
            {
                result.Add(slot);
            }
        }

        return result;
    }

    public List<ParkingSlot> GetSlotsByState(ParkingSlotState state)
    {
        List<ParkingSlot> result = new List<ParkingSlot>();

        foreach (ParkingSlot slot in allSlots)
        {
            if (slot == null)
            {
                continue;
            }

            if (slot.state == state)
            {
                result.Add(slot);
            }
        }

        return result;
    }

    public int GetSlotCountByState(ParkingSlotState state)
    {
        int count = 0;

        foreach (ParkingSlot slot in allSlots)
        {
            if (slot == null)
            {
                continue;
            }

            if (slot.state == state)
            {
                count++;
            }
        }

        return count;
    }

    public ParkingSlot GetFirstAvailableSlot()
    {
        foreach (ParkingSlot slot in allSlots)
        {
            if (slot == null)
            {
                continue;
            }

            if (slot.IsAvailable())
            {
                return slot;
            }
        }

        return null;
    }

    public ParkingSlot GetFirstAvailableSlotWithAccessWaypoint()
    {
        foreach (ParkingSlot slot in allSlots)
        {
            if (!IsSelectableSlot(slot))
            {
                continue;
            }

            return slot;
        }

        return null;
    }

    public ParkingSlot GetRandomAvailableSlotWithAccessWaypoint()
    {
        List<ParkingSlot> availableSlots = GetAvailableSlotsWithAccessWaypoint();

        if (availableSlots.Count == 0)
        {
            return null;
        }

        int index = Random.Range(0, availableSlots.Count);
        return availableSlots[index];
    }

    public List<ParkingSlot> GetAvailableSlotsWithAccessWaypoint()
    {
        List<ParkingSlot> availableSlots = new List<ParkingSlot>();

        foreach (ParkingSlot slot in allSlots)
        {
            if (!IsSelectableSlot(slot))
            {
                continue;
            }

            availableSlots.Add(slot);
        }

        return availableSlots;
    }

    public ParkingSlot GetRandomAvailableSlotInArea(string areaId)
    {
        List<ParkingSlot> candidates = new List<ParkingSlot>();

        foreach (ParkingSlot slot in allSlots)
        {
            if (!IsSelectableSlot(slot))
            {
                continue;
            }

            if (slot.areaId != areaId)
            {
                continue;
            }

            candidates.Add(slot);
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        int index = Random.Range(0, candidates.Count);
        return candidates[index];
    }

    public bool TryReserveSlot(ParkingSlot slot)
    {
        if (!IsSelectableSlot(slot))
        {
            return false;
        }

        slot.SetReserved();
        return true;
    }

    public bool TryReserveSlot(ParkingSlot slot, NPC_CarController owner)
    {
        if (!IsSelectableSlot(slot))
        {
            return false;
        }

        return slot.TryReserve(owner);
    }

    public ParkingSlot ReserveFirstAvailableSlot()
    {
        ParkingSlot slot = GetFirstAvailableSlotWithAccessWaypoint();

        if (slot == null)
        {
            return null;
        }

        slot.SetReserved();
        return slot;
    }

    public ParkingSlot ReserveRandomAvailableSlot()
    {
        ParkingSlot slot = GetRandomAvailableSlotWithAccessWaypoint();

        if (slot == null)
        {
            return null;
        }

        slot.SetReserved();
        return slot;
    }

    public ParkingSlot ReserveRandomAvailableSlotInArea(string areaId)
    {
        ParkingSlot slot = GetRandomAvailableSlotInArea(areaId);

        if (slot == null)
        {
            return null;
        }

        slot.SetReserved();
        return slot;
    }

    public void ReleaseReservation(ParkingSlot slot)
    {
        if (slot == null)
        {
            return;
        }

        if (slot.state == ParkingSlotState.Reserved)
        {
            slot.ForceRelease();
        }
    }

    public bool IsSelectableSlot(ParkingSlot slot)
    {
        if (slot == null)
        {
            return false;
        }

        if (!slot.IsAvailable())
        {
            return false;
        }

        if (slot.parkingPoint == null)
        {
            return false;
        }

        if (slot.accessWaypoint == null)
        {
            return false;
        }

        if (usePhysicalSlotCheck)
        {
            if (slot.HasCarInSlot(carLayerMask, slotCheckBoxSize))
            {
                return false;
            }
        }

        return true;
    }

    [ContextMenu("Print Slot Summary")]
    public void PrintSlotSummary()
    {
        Debug.Log("===== Parking Slot Summary =====");
        Debug.Log($"Total: {allSlots.Count}");
        Debug.Log($"Empty: {GetSlotCountByState(ParkingSlotState.Empty)}");
        Debug.Log($"Reserved: {GetSlotCountByState(ParkingSlotState.Reserved)}");
        Debug.Log($"Occupied: {GetSlotCountByState(ParkingSlotState.Occupied)}");
        Debug.Log($"Disabled: {GetSlotCountByState(ParkingSlotState.Disabled)}");
    }
}
