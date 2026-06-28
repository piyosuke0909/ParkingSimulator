using UnityEngine;

public class TrafficBlock : MonoBehaviour
{
    [Header("Traffic Block Info")]
    public string blockId;

    [Header("State")]
    public NPC_CarController reservedBy;
    public NPC_CarController occupiedBy;

    [Header("Debug")]
    public bool logDebug = false;

    public bool TryReserve(NPC_CarController car)
    {
        if (car == null)
        {
            return false;
        }

        if (occupiedBy != null && occupiedBy != car)
        {
            return false;
        }

        if (reservedBy != null && reservedBy != car)
        {
            return false;
        }

        reservedBy = car;

        if (logDebug)
        {
            Debug.Log($"{name}: reserved by {car.name}", this);
        }

        return true;
    }

    public void Enter(NPC_CarController car)
    {
        if (car == null)
        {
            return;
        }

        if (occupiedBy != null && occupiedBy != car)
        {
            Debug.LogWarning(
                $"{name}: occupiedBy が別の車のため Enter を拒否しました。NewCar={car.name}, OccupiedBy={occupiedBy.name}",
                this
            );
            return;
        }

        occupiedBy = car;

        if (reservedBy == car)
        {
            reservedBy = null;
        }

        if (logDebug)
        {
            Debug.Log($"{name}: entered by {car.name}", this);
        }
    }

    public void Release(NPC_CarController car)
    {
        if (car == null)
        {
            return;
        }

        if (reservedBy == car)
        {
            reservedBy = null;
        }

        if (occupiedBy == car)
        {
            occupiedBy = null;
        }

        if (logDebug)
        {
            Debug.Log($"{name}: released by {car.name}", this);
        }
    }

    public bool IsAvailableFor(NPC_CarController car)
    {
        if (car == null)
        {
            return false;
        }

        if (occupiedBy != null && occupiedBy != car)
        {
            return false;
        }

        if (reservedBy != null && reservedBy != car)
        {
            return false;
        }

        return true;
    }

    public bool IsReservedOrOccupiedByOtherCar(NPC_CarController car)
    {
        if (car == null)
        {
            return reservedBy != null || occupiedBy != null;
        }

        return (reservedBy != null && reservedBy != car) ||
               (occupiedBy != null && occupiedBy != car);
    }

    private void OnDrawGizmos()
    {
        if (occupiedBy != null)
        {
            Gizmos.color = Color.red;
        }
        else if (reservedBy != null)
        {
            // 予約中。黄色は他のGizmoと被りやすいため青にしています。
            Gizmos.color = new Color(1f, 0.5f, 0f);
        }
        else
        {
            Gizmos.color = Color.cyan;
        }

        Gizmos.DrawWireCube(transform.position, transform.localScale);
    }
}
