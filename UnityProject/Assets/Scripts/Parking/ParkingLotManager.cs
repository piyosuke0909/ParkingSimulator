using System.Collections.Generic;
using UnityEngine;

public class ParkingLotManager : MonoBehaviour
{
    public List<ParkingSlot> allSlots = new List<ParkingSlot>();

    private Dictionary<string, ParkingSlot> slotDictionary = new Dictionary<string, ParkingSlot>();

    private void Awake()
    {
        RegisterAllSlots();
    }

    public void RegisterAllSlots()
    {
        allSlots.Clear();
        slotDictionary.Clear();

        ParkingSlot[] slots = FindObjectsOfType<ParkingSlot>();

        foreach (ParkingSlot slot in slots)
        {
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

        Debug.Log($"ParkingSlotìoò^äÆóπ: {allSlots.Count}åè");
    }

    public ParkingSlot GetSlotById(string slotId)
    {
        if (slotDictionary.TryGetValue(slotId, out ParkingSlot slot))
        {
            return slot;
        }

        Debug.LogWarning($"éwíËÇ≥ÇÍÇΩSlot IDÇ™å©Ç¬Ç©ÇËÇ‹ÇπÇÒ: {slotId}");
        return null;
    }

}