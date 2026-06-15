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

    [Header("Points")]
    public Transform parkingPoint;
    public Transform detectionPoint;

    [Header("Waypoint")]
    public Waypoint accessWaypoint;

    [Header("Visual Target")]
    public Renderer slotBaseRenderer;

    [Header("Colors")]
    public Color emptyColor = Color.gray;
    public Color occupiedColor = Color.red;
    public Color reservedColor = Color.yellow;
    public Color disabledColor = Color.blue;

    [Header("Empty Display")]
    public bool hideSlotBaseWhenEmpty = false;

    private void Awake()
    {
        FindReferences();
    }

    private void Start()
    {
        UpdateVisual();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        FindReferences();
    }
#endif

    private void FindReferences()
    {
        if (slotBaseRenderer == null)
        {
            Transform slotBase = transform.Find("Slot_Base");

            if (slotBase != null)
            {
                slotBaseRenderer = slotBase.GetComponentInChildren<Renderer>(true);
            }
        }

        if (parkingPoint == null)
        {
            Transform point = transform.Find("ParkingPoint");

            if (point != null)
            {
                parkingPoint = point;
            }
        }

        if (detectionPoint == null)
        {
            Transform point = transform.Find("DetectionPoint");

            if (point != null)
            {
                detectionPoint = point;
            }
        }
    }

    public bool IsAvailable()
    {
        return state == ParkingSlotState.Empty;
    }

    public void SetEmpty()
    {
        SetState(ParkingSlotState.Empty);
    }

    public void SetOccupied()
    {
        SetState(ParkingSlotState.Occupied);
    }

    public void SetReserved()
    {
        SetState(ParkingSlotState.Reserved);
    }

    public void SetDisabled()
    {
        SetState(ParkingSlotState.Disabled);
    }

    public void SetState(ParkingSlotState newState)
    {
        if (state == newState)
        {
            return;
        }

        state = newState;
        UpdateVisual();
    }

    public void UpdateVisual()
    {
        FindReferences();

        if (slotBaseRenderer == null)
        {
            return;
        }

        switch (state)
        {
            case ParkingSlotState.Empty:
                ApplyEmptyVisual();
                break;

            case ParkingSlotState.Occupied:
                ApplyColorVisual(occupiedColor);
                break;

            case ParkingSlotState.Reserved:
                ApplyColorVisual(reservedColor);
                break;

            case ParkingSlotState.Disabled:
                ApplyColorVisual(disabledColor);
                break;
        }
    }

    private void ApplyEmptyVisual()
    {
        if (slotBaseRenderer == null)
        {
            return;
        }

        if (hideSlotBaseWhenEmpty)
        {
            slotBaseRenderer.gameObject.SetActive(false);
        }
        else
        {
            slotBaseRenderer.gameObject.SetActive(true);
            slotBaseRenderer.material.color = emptyColor;
        }
    }

    private void ApplyColorVisual(Color color)
    {
        slotBaseRenderer.gameObject.SetActive(true);
        slotBaseRenderer.material.color = color;
    }
}