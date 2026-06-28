using UnityEngine;

public enum ParkingSlotState
{
    Empty,
    Occupied,
    Reserved,
    Leaving,
    Disabled
}

public class ParkingSlot : MonoBehaviour
{
    [Header("Slot Info")]
    public string slotId;
    public string areaId;

    [Header("State")]
    public ParkingSlotState state = ParkingSlotState.Empty;

    [Tooltip("CameraParkingSensorが判定した、物理的に車がいるかどうかです。")]
    public bool sensorOccupied;

    [Tooltip("出庫中かどうかです。出庫中はセンサー上Emptyでも予約不可にします。")]
    public bool isLeaving;

    [Header("Runtime Owner")]
    public NPC_CarController reservedBy;
    public NPC_CarController occupiedBy;

    [Header("Points")]
    public Transform parkingPoint;
    public Transform detectionPoint;

    [Header("Waypoint")]
    public Waypoint accessWaypoint;

    [Header("Visual")]
    public Renderer slotBaseRenderer;
    public bool hideSlotBaseWhenEmpty = false;

    public Color emptyColor = Color.white;
    public Color occupiedColor = Color.red;
    public Color reservedColor = Color.yellow;

    [Tooltip("出庫中の見た目です。空きに見せたい場合は Empty Color と同じ白にしてください。")]
    public Color leavingColor = Color.white;

    public Color disabledColor = Color.blue;

    [Header("Debug")]
    public bool logStateChange = false;

    private MaterialPropertyBlock propertyBlock;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        FindReferences();
        RefreshStateFromSensorAndReservation();
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
                slotBaseRenderer = slotBase.GetComponent<Renderer>();
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

        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }
    }

    public bool IsAvailable()
    {
        return state != ParkingSlotState.Disabled &&
               !sensorOccupied &&
               !isLeaving &&
               reservedBy == null &&
               occupiedBy == null;
    }

    public bool IsPhysicallyOccupied()
    {
        return sensorOccupied;
    }

    public bool HasReservationOrOwner()
    {
        return reservedBy != null || occupiedBy != null || isLeaving;
    }

    public bool IsReservedBy(NPC_CarController carController)
    {
        return carController != null && reservedBy == carController;
    }

    public bool IsOccupiedBy(NPC_CarController carController)
    {
        return carController != null && occupiedBy == carController;
    }

    public bool IsOwnedBy(NPC_CarController carController)
    {
        if (carController == null)
        {
            return false;
        }

        return reservedBy == carController || occupiedBy == carController;
    }

    public bool TryReserve(NPC_CarController carController)
    {
        if (carController == null)
        {
            return false;
        }

        if (!IsAvailable())
        {
            return false;
        }

        reservedBy = carController;
        occupiedBy = null;
        isLeaving = false;

        RefreshStateFromSensorAndReservation();

        return true;
    }

    public bool TryAssignReservedOwner(NPC_CarController carController)
    {
        if (carController == null)
        {
            return false;
        }

        if (state == ParkingSlotState.Disabled)
        {
            return false;
        }

        if (reservedBy != null && reservedBy != carController)
        {
            return false;
        }

        if (occupiedBy != null && occupiedBy != carController)
        {
            return false;
        }

        reservedBy = carController;
        occupiedBy = null;
        isLeaving = false;

        RefreshStateFromSensorAndReservation();

        return true;
    }

    public bool TryOccupy(NPC_CarController carController)
    {
        if (carController == null)
        {
            return false;
        }

        if (state == ParkingSlotState.Disabled)
        {
            return false;
        }

        if (occupiedBy != null && occupiedBy != carController)
        {
            return false;
        }

        if (reservedBy != null && reservedBy != carController)
        {
            return false;
        }

        // CameraParkingSensorに空き判定を統一するため、
        // ここではOccupied/Emptyを直接決めない。
        // このスロットの使用者だけを登録する。
        reservedBy = null;
        occupiedBy = carController;
        isLeaving = false;

        RefreshStateFromSensorAndReservation();

        return true;
    }

    public void SetLeaving(NPC_CarController carController)
    {
        if (carController == null)
        {
            return;
        }

        if (occupiedBy != null && occupiedBy != carController)
        {
            return;
        }

        if (reservedBy != null && reservedBy != carController)
        {
            return;
        }

        reservedBy = null;
        occupiedBy = carController;
        isLeaving = true;

        RefreshStateFromSensorAndReservation();
    }

    public bool TryRelease(NPC_CarController carController)
    {
        if (carController == null)
        {
            return false;
        }

        if (reservedBy != null && reservedBy != carController)
        {
            return false;
        }

        if (occupiedBy != null && occupiedBy != carController)
        {
            return false;
        }

        reservedBy = null;
        occupiedBy = null;
        isLeaving = false;

        RefreshStateFromSensorAndReservation();

        return true;
    }

    public bool TryReleaseAfterExit(NPC_CarController carController)
    {
        if (carController == null)
        {
            return false;
        }

        if (reservedBy == carController || occupiedBy == carController)
        {
            reservedBy = null;
            occupiedBy = null;
            isLeaving = false;

            // Empty/OccupiedはCameraParkingSensorのsensorOccupiedに従う。
            RefreshStateFromSensorAndReservation();
            return true;
        }

        if (reservedBy == null && occupiedBy == null)
        {
            isLeaving = false;
            RefreshStateFromSensorAndReservation();
            return true;
        }

        return false;
    }

    public void ForceRelease()
    {
        reservedBy = null;
        occupiedBy = null;
        isLeaving = false;

        RefreshStateFromSensorAndReservation();
    }

    // CameraParkingSensorから呼ぶ想定です。
    public void SetSensorOccupied(bool occupied)
    {
        if (sensorOccupied == occupied)
        {
            return;
        }

        sensorOccupied = occupied;
        RefreshStateFromSensorAndReservation();

        if (logStateChange)
        {
            Debug.Log($"{name}: sensorOccupied = {sensorOccupied}, state = {state}", this);
        }
    }

    // 互換用。CameraParkingSensorなど外部センサーからのOccupied判定。
    public void SetOccupied()
    {
        SetSensorOccupied(true);
    }

    // 互換用。CameraParkingSensorなど外部センサーからのEmpty判定。
    public void SetEmpty()
    {
        SetSensorOccupied(false);
    }

    public void SetReserved()
    {
        if (state == ParkingSlotState.Disabled)
        {
            return;
        }

        RefreshStateFromSensorAndReservation();
    }

    public void SetDisabled()
    {
        reservedBy = null;
        occupiedBy = null;
        isLeaving = false;
        sensorOccupied = false;

        SetState(ParkingSlotState.Disabled);
    }

    public void SetEnabledAsEmpty()
    {
        reservedBy = null;
        occupiedBy = null;
        isLeaving = false;
        sensorOccupied = false;

        SetState(ParkingSlotState.Empty);
    }

    public void RefreshStateFromSensorAndReservation()
    {
        if (state == ParkingSlotState.Disabled)
        {
            UpdateVisual();
            return;
        }

        if (sensorOccupied)
        {
            SetState(ParkingSlotState.Occupied);
            return;
        }

        if (isLeaving)
        {
            SetState(ParkingSlotState.Leaving);
            return;
        }

        if (reservedBy != null || occupiedBy != null)
        {
            SetState(ParkingSlotState.Reserved);
            return;
        }

        SetState(ParkingSlotState.Empty);
    }

    public void SetState(ParkingSlotState newState)
    {
        ParkingSlotState oldState = state;
        state = newState;
        UpdateVisual();

        if (logStateChange && oldState != newState)
        {
            Debug.Log($"{name}: state {oldState} -> {newState}", this);
        }
    }

    public bool HasCarInSlot(LayerMask carLayerMask, Vector3 checkBoxSize)
    {
        Transform checkTransform = detectionPoint != null ? detectionPoint : parkingPoint;

        if (checkTransform == null)
        {
            return false;
        }

        Collider[] hits = Physics.OverlapBox(
            checkTransform.position,
            checkBoxSize * 0.5f,
            checkTransform.rotation,
            carLayerMask
        );

        foreach (Collider hit in hits)
        {
            Car hitCar = hit.GetComponentInParent<Car>();

            if (hitCar == null)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private void UpdateVisual()
    {
        FindReferences();

        if (slotBaseRenderer == null)
        {
            return;
        }

        if (hideSlotBaseWhenEmpty && state == ParkingSlotState.Empty)
        {
            slotBaseRenderer.enabled = false;
            return;
        }

        slotBaseRenderer.enabled = true;

        Color targetColor = GetStateColor();

        slotBaseRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorId, targetColor);
        propertyBlock.SetColor(ColorId, targetColor);
        slotBaseRenderer.SetPropertyBlock(propertyBlock);
    }

    private Color GetStateColor()
    {
        switch (state)
        {
            case ParkingSlotState.Empty:
                return emptyColor;

            case ParkingSlotState.Occupied:
                return occupiedColor;

            case ParkingSlotState.Reserved:
                return reservedColor;

            case ParkingSlotState.Leaving:
                return leavingColor;

            case ParkingSlotState.Disabled:
                return disabledColor;

            default:
                return emptyColor;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Transform checkTransform = detectionPoint != null ? detectionPoint : parkingPoint;

        if (checkTransform == null)
        {
            return;
        }

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.75f);
        Gizmos.DrawWireCube(checkTransform.position, new Vector3(10f, 4f, 8f));
    }
}
