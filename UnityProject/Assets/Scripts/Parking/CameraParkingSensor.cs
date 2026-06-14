using System.Collections.Generic;
using UnityEngine;

public class CameraParkingSensor : MonoBehaviour
{
    [Header("Area Root")]
    public Transform areaRoot;

    [Header("Target Slots")]
    public List<ParkingSlot> targetSlots = new List<ParkingSlot>();

    [Header("Detection Settings")]
    public LayerMask carLayerMask;
    public float checkInterval = 0.2f;
    public float requiredDetectionTime = 1.5f;
    public float lostDetectionTime = 3.0f;

    [Header("Debug")]
    public bool drawDebugRay = true;
    public bool logStateChange = false;

    private Dictionary<ParkingSlot, float> detectedTimers = new Dictionary<ParkingSlot, float>();
    private Dictionary<ParkingSlot, float> lostTimers = new Dictionary<ParkingSlot, float>();

    private float checkTimer;

    private void Start()
    {
        AutoCollectSlots();
        InitializeTimers();
    }

    private void AutoCollectSlots()
    {
        targetSlots.Clear();

        if (areaRoot == null)
        {
            Debug.LogWarning($"{name}: areaRoot が設定されていません。");
            return;
        }

        ParkingSlot[] slots = areaRoot.GetComponentsInChildren<ParkingSlot>();

        foreach (ParkingSlot slot in slots)
        {
            targetSlots.Add(slot);
        }

        Debug.Log($"{name}: {targetSlots.Count}個のスロットを監視対象にしました。");
    }

    private void InitializeTimers()
    {
        detectedTimers.Clear();
        lostTimers.Clear();

        foreach (ParkingSlot slot in targetSlots)
        {
            detectedTimers[slot] = 0f;
            lostTimers[slot] = lostDetectionTime;
        }
    }

    private void Update()
    {
        checkTimer += Time.deltaTime;

        if (checkTimer < checkInterval)
        {
            return;
        }

        checkTimer = 0f;
        CheckSlots();
    }

    private void CheckSlots()
    {
        foreach (ParkingSlot slot in targetSlots)
        {
            if (slot == null || slot.detectionPoint == null)
            {
                continue;
            }

            bool detected = DetectCarAtSlot(slot);

            if (detected)
            {
                detectedTimers[slot] += checkInterval;
                lostTimers[slot] = 0f;

                if (detectedTimers[slot] >= requiredDetectionTime)
                {
                    if (slot.state != ParkingSlotState.Occupied)
                    {
                        slot.SetOccupied();
                    }
                }
            }
            else
            {
                lostTimers[slot] += checkInterval;
                detectedTimers[slot] = 0f;

                if (lostTimers[slot] >= lostDetectionTime)
                {
                    if (slot.state != ParkingSlotState.Empty)
                    {
                        slot.SetEmpty();
                    }
                }
            }
        }
    }

    private bool DetectCarAtSlot(ParkingSlot slot)
    {
        Vector3 origin = transform.position;
        Vector3 target = slot.detectionPoint.position;
        Vector3 direction = target - origin;
        float distance = direction.magnitude;

        Ray ray = new Ray(origin, direction.normalized);

        if (drawDebugRay)
        {
            Debug.DrawRay(origin, direction, Color.green, checkInterval);
        }

        if (Physics.Raycast(ray, out RaycastHit hit, distance, carLayerMask))
        {
            Car car = hit.collider.GetComponentInParent<Car>();

            if (car != null)
            {
                return true;
            }
        }

        return false;
    }
}