using UnityEngine;

public enum ParkingSlotState
{
    Empty,
    Occupied,
    Reserved,
    Disabled
}

public class ParkingSlot : MonoBehaviour
{
    [Header("Slot Info")]
    public string slotId;
    public string areaId;

    [Header("State")]
    public ParkingSlotState state = ParkingSlotState.Empty;

    [Header("Parking Point")]
    public Transform parkingPoint;

    [Header("Visual")]
    public Renderer slotBaseRenderer;

    public Color emptyColor = Color.gray;
    public Color occupiedColor = Color.red;
    public Color reservedColor = Color.yellow;
    public Color disabledColor = Color.black;

    private void Start()
    {
        UpdateVisual();
    }

    public bool IsAvailable()
    {
        return state == ParkingSlotState.Empty;
    }

    public void SetEmpty()
    {
        state = ParkingSlotState.Empty;
        UpdateVisual();
    }

    public void SetOccupied()
    {
        state = ParkingSlotState.Occupied;
        UpdateVisual();
    }

    public void SetReserved()
    {
        state = ParkingSlotState.Reserved;
        UpdateVisual();
    }

    public void SetDisabled()
    {
        state = ParkingSlotState.Disabled;
        UpdateVisual();
    }

    public void UpdateVisual()
    {
        if (slotBaseRenderer == null)
        {
            Transform slotBase = transform.Find("Slot_Base");

            if (slotBase != null)
            {
                slotBaseRenderer = slotBase.GetComponent<Renderer>();
            }
        }

        if (slotBaseRenderer == null)
        {
            Debug.LogWarning($"Slot_Base ‚Ì Renderer ‚ªŒ©‚Â‚©‚è‚Ü‚¹‚ñ: {gameObject.name}");
            return;
        }

        switch (state)
        {
            case ParkingSlotState.Empty:
                slotBaseRenderer.material.color = emptyColor;
                break;

            case ParkingSlotState.Occupied:
                slotBaseRenderer.material.color = occupiedColor;
                break;

            case ParkingSlotState.Reserved:
                slotBaseRenderer.material.color = reservedColor;
                break;

            case ParkingSlotState.Disabled:
                slotBaseRenderer.material.color = disabledColor;
                break;
        }

    }

}