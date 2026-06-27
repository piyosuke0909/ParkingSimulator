using UnityEngine;

public class RoadSection : MonoBehaviour
{
    [Header("Road Section Info")]
    public string sectionId;

    [Header("State")]
    public NPC_CarController reservedBy;

    [Header("Debug")]
    public bool logDebug = false;

    public bool TryReserve(NPC_CarController car)
    {
        if (car == null)
        {
            return false;
        }

        if (reservedBy == null || reservedBy == car)
        {
            reservedBy = car;

            if (logDebug)
            {
                Debug.Log($"{name}: reserved by {car.name}", this);
            }

            return true;
        }

        return false;
    }

    public void Release(NPC_CarController car)
    {
        if (car == null)
        {
            return;
        }

        if (reservedBy == car)
        {
            if (logDebug)
            {
                Debug.Log($"{name}: released by {car.name}", this);
            }

            reservedBy = null;
        }
    }

    public bool IsReservedByOtherCar(NPC_CarController car)
    {
        return reservedBy != null && reservedBy != car;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = reservedBy == null ? Color.cyan : Color.red;
        Gizmos.DrawWireCube(transform.position, transform.localScale);
    }
}