using UnityEngine;

public class ParkingLotSearchTest : MonoBehaviour
{
    public ParkingLotManager parkingLotManager;

    [Header("Test Target")]
    public string targetSlotId = "A-001";

    private void Start()
    {
        if (parkingLotManager == null)
        {
            Debug.LogError("ParkingLotManagerが設定されていません。");
            return;
        }

        ParkingSlot slot = parkingLotManager.GetSlotById(targetSlotId);

        if (slot == null)
        {
            Debug.LogError($"{targetSlotId} が見つかりませんでした。");
            return;
        }

        Debug.Log($"検索成功: {slot.slotId} / Area: {slot.areaId} / State: {slot.state}");

        slot.SetOccupied();

        Debug.Log($"{slot.slotId} を Occupied に変更しました。");
    }
}