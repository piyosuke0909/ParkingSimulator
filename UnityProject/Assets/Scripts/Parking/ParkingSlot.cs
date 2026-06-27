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

    [Header("Runtime Owner")]
    public NPC_CarController reservedBy;
    public NPC_CarController occupiedBy;

    [Header("Points")]
    public Transform parkingPoint;
    public Transform detectionPoint;

    [Header("Waypoint")]
    public Waypoint accessWaypoint;

    private void Awake()
    {
        FindReferences();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        FindReferences();
    }
#endif

    private void FindReferences()
    {
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
        return state == ParkingSlotState.Empty &&
               reservedBy == null &&
               occupiedBy == null;
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
        state = ParkingSlotState.Reserved;

        return true;
    }

    public bool TryAssignReservedOwner(NPC_CarController carController)
    {
        if (carController == null)
        {
            return false;
        }

        if (state != ParkingSlotState.Reserved)
        {
            return false;
        }

        if (reservedBy != null && reservedBy != carController)
        {
            return false;
        }

        reservedBy = carController;
        occupiedBy = null;

        return true;
    }

    public bool TryOccupy(NPC_CarController carController)
    {
        if (carController == null)
        {
            return false;
        }

        if (state == ParkingSlotState.Occupied)
        {
            if (occupiedBy == carController)
            {
                return true;
            }

            // CameraParkingSensorなどが先にOccupiedへ変えた場合でも、
            // 予約者がこの車なら駐車完了を許可する。
            if (occupiedBy == null && reservedBy == carController)
            {
                reservedBy = null;
                occupiedBy = carController;
                state = ParkingSlotState.Occupied;
                return true;
            }

            return false;
        }

        if (state != ParkingSlotState.Reserved)
        {
            return false;
        }

        if (reservedBy != carController)
        {
            return false;
        }

        reservedBy = null;
        occupiedBy = carController;
        state = ParkingSlotState.Occupied;

        return true;
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
        state = ParkingSlotState.Empty;

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
            state = ParkingSlotState.Empty;
            return true;
        }

        // 所有者情報がすでに消えているが、状態だけがOccupiedに残っている場合の保険。
        if (reservedBy == null &&
            occupiedBy == null &&
            state == ParkingSlotState.Occupied)
        {
            state = ParkingSlotState.Empty;
            return true;
        }

        return false;
    }

    public void ForceRelease()
    {
        reservedBy = null;
        occupiedBy = null;
        state = ParkingSlotState.Empty;
    }

    public void SetEmpty()
    {
        // 予約中・使用中のSlotは、外部センサーなどから勝手にEmptyへ戻さない。
        if (reservedBy != null || occupiedBy != null)
        {
            return;
        }

        state = ParkingSlotState.Empty;
    }

    public void SetOccupied()
    {
        // 予約者・使用者情報は消さない。
        state = ParkingSlotState.Occupied;
    }

    public void SetReserved()
    {
        if (occupiedBy != null)
        {
            return;
        }

        state = ParkingSlotState.Reserved;
    }

    public void SetDisabled()
    {
        reservedBy = null;
        occupiedBy = null;
        state = ParkingSlotState.Disabled;
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
}
