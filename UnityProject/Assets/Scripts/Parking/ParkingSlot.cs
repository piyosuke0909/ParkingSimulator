using UnityEngine;

public enum ParkingSlotState
{
    Empty,
    Occupied,
    Reserved,
    Disabled
}

public enum ParkingManeuverPreference
{
    Auto,
    FrontInOnly,
    ReverseInOnly,
    PreferFrontIn,
    PreferReverseIn
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

    [Header("Detection")]
    public Transform detectionPoint;

    [Header("NPC Route")]
    public Transform approachPoint;
    public Transform frontEntryPoint;
    public Transform reverseEntryPoint;
    public string roadNodeId;

    [Header("NPC Maneuver")]
    public float slotWidth = 2.5f;
    public float slotDepth = 5.0f;
    public float aisleWidth = 6.0f;
    public bool allowFrontIn = true;
    public bool allowReverseIn = true;
    public ParkingManeuverPreference preferredManeuver = ParkingManeuverPreference.Auto;
    public ParkingSlotGeometry geometry;

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

    public bool TryReserve()
    {
        if (!IsAvailable())
        {
            return false;
        }

        SetReserved();
        return true;
    }

    public Transform GetApproachPoint()
    {
        if (approachPoint != null)
        {
            return approachPoint;
        }

        if (parkingPoint != null)
        {
            return parkingPoint;
        }

        return transform;
    }

    public Transform GetParkingPoint()
    {
        if (parkingPoint != null)
        {
            return parkingPoint;
        }

        return transform;
    }

    public ParkingSlotGeometry GetGeometry()
    {
        if (geometry != null)
        {
            return geometry;
        }

        geometry = GetComponentInChildren<ParkingSlotGeometry>();
        return geometry;
    }

    public ManeuverPath[] GetManeuverPaths()
    {
        return GetComponentsInChildren<ManeuverPath>();
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
            Debug.LogWarning($"Slot_Base の Renderer が見つかりません: {gameObject.name}");
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

    private void OnValidate()
    {
        slotWidth = Mathf.Max(0.01f, slotWidth);
        slotDepth = Mathf.Max(0.01f, slotDepth);
        aisleWidth = Mathf.Max(0.01f, aisleWidth);
    }

}
