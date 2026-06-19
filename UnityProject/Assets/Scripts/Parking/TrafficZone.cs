using System.Collections.Generic;
using UnityEngine;

public enum TrafficZoneType
{
    Normal,
    TwoWayRoad,
    Intersection
}

public class TrafficZone : MonoBehaviour
{
    [Header("Zone Info")]
    public string zoneId;
    public TrafficZoneType zoneType = TrafficZoneType.Normal;

    [Header("State")]
    public NPC_CarController currentUser;

    [Header("Waiting Queue")]
    public List<NPC_CarController> priorityWaitingCars = new List<NPC_CarController>();
    public List<NPC_CarController> normalWaitingCars = new List<NPC_CarController>();

    [Header("Debug")]
    public bool logDebug = false;

    public bool IsFree()
    {
        return currentUser == null;
    }

    public bool IsUsedBy(NPC_CarController car)
    {
        return currentUser == car;
    }

    public bool IsUsedByOtherCar(NPC_CarController car)
    {
        return currentUser != null && currentUser != car;
    }

    public bool IsWaiting(NPC_CarController car)
    {
        if (car == null)
        {
            return false;
        }

        return priorityWaitingCars.Contains(car) || normalWaitingCars.Contains(car);
    }

    public bool IsFirstInAnyQueue(NPC_CarController car)
    {
        if (car == null)
        {
            return false;
        }

        if (priorityWaitingCars.Count > 0)
        {
            return priorityWaitingCars[0] == car;
        }

        if (normalWaitingCars.Count > 0)
        {
            return normalWaitingCars[0] == car;
        }

        return false;
    }

    public void AddToQueue(NPC_CarController car, bool priority)
    {
        if (car == null)
        {
            return;
        }

        if (currentUser == car)
        {
            return;
        }

        RemoveFromQueue(car);

        if (priority)
        {
            priorityWaitingCars.Add(car);

            if (logDebug)
            {
                Debug.Log($"{zoneId}: 優先待機キュー追加 {car.name} / PriorityCount={priorityWaitingCars.Count}");
            }
        }
        else
        {
            normalWaitingCars.Add(car);

            if (logDebug)
            {
                Debug.Log($"{zoneId}: 通常待機キュー追加 {car.name} / NormalCount={normalWaitingCars.Count}");
            }
        }
    }

    public void RemoveFromQueue(NPC_CarController car)
    {
        if (car == null)
        {
            return;
        }

        bool removedPriority = priorityWaitingCars.Remove(car);
        bool removedNormal = normalWaitingCars.Remove(car);

        if (logDebug && (removedPriority || removedNormal))
        {
            Debug.Log($"{zoneId}: 待機キュー削除 {car.name}");
        }
    }

    public bool TryEnter(NPC_CarController car, bool priority)
    {
        if (car == null)
        {
            return false;
        }

        if (currentUser == car)
        {
            RemoveFromQueue(car);
            return true;
        }

        if (currentUser != null)
        {
            AddToQueue(car, priority);

            if (logDebug)
            {
                Debug.Log($"{zoneId}: 使用中のため待機 Current={currentUser.name}, Request={car.name}, Priority={priority}");
            }

            return false;
        }

        if (priorityWaitingCars.Count > 0)
        {
            if (priorityWaitingCars[0] != car)
            {
                AddToQueue(car, priority);

                if (logDebug)
                {
                    Debug.Log($"{zoneId}: 優先キュー待ち Request={car.name}, First={priorityWaitingCars[0].name}");
                }

                return false;
            }
        }
        else if (normalWaitingCars.Count > 0)
        {
            if (normalWaitingCars[0] != car)
            {
                AddToQueue(car, priority);

                if (logDebug)
                {
                    Debug.Log($"{zoneId}: 通常キュー待ち Request={car.name}, First={normalWaitingCars[0].name}");
                }

                return false;
            }
        }

        RemoveFromQueue(car);
        currentUser = car;

        if (logDebug)
        {
            Debug.Log($"{zoneId}: 進入許可 {car.name}, Priority={priority}");
        }

        return true;
    }

    public void Exit(NPC_CarController car)
    {
        if (car == null)
        {
            return;
        }

        RemoveFromQueue(car);

        if (currentUser == car)
        {
            if (logDebug)
            {
                Debug.Log($"{zoneId}: 解放 {car.name}");
            }

            currentUser = null;
        }
    }

    public void ClearQueue()
    {
        priorityWaitingCars.Clear();
        normalWaitingCars.Clear();

        if (logDebug)
        {
            Debug.Log($"{zoneId}: 待機キューをクリアしました。");
        }
    }

    private void OnDrawGizmos()
    {
        if (zoneType == TrafficZoneType.Intersection)
        {
            Gizmos.color = new Color(1f, 0.3f, 0.1f, 0.35f);
        }
        else if (zoneType == TrafficZoneType.TwoWayRoad)
        {
            Gizmos.color = new Color(0.3f, 0.5f, 1f, 0.35f);
        }
        else
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.25f);
        }

        Gizmos.DrawCube(transform.position, transform.localScale);
    }
}