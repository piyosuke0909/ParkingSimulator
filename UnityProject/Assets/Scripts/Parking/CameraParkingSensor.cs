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

    [Header("Ray Detection")]
    public bool useRaycastDetection = true;

    [Tooltip("Rayを少し上から飛ばします。地面や低いColliderに当たりすぎる場合に調整します。")]
    public Vector3 rayOriginOffset = Vector3.zero;

    [Header("OverlapBox Fallback")]
    [Tooltip("Rayだけでは不安定な場合に、スロット範囲内の車Colliderも確認します。")]
    public bool useOverlapBoxDetection = true;

    public Vector3 overlapBoxSize = new Vector3(4.5f, 3.0f, 7.0f);

    [Header("Debug")]
    public bool drawDebugRay = true;
    public bool drawDebugBox = true;
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
            if (slot == null)
            {
                continue;
            }

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
            if (slot == null)
            {
                continue;
            }

            if (!detectedTimers.ContainsKey(slot))
            {
                detectedTimers[slot] = 0f;
            }

            if (!lostTimers.ContainsKey(slot))
            {
                lostTimers[slot] = lostDetectionTime;
            }

            bool detected = DetectCarAtSlot(slot);

            if (detected)
            {
                detectedTimers[slot] += checkInterval;
                lostTimers[slot] = 0f;

                if (detectedTimers[slot] >= requiredDetectionTime)
                {
                    if (!slot.sensorOccupied)
                    {
                        slot.SetSensorOccupied(true);

                        if (logStateChange)
                        {
                            Debug.Log($"{name}: {slot.slotId} をOccupied判定にしました。", this);
                        }
                    }
                }
            }
            else
            {
                lostTimers[slot] += checkInterval;
                detectedTimers[slot] = 0f;

                if (lostTimers[slot] >= lostDetectionTime)
                {
                    if (slot.sensorOccupied)
                    {
                        slot.SetSensorOccupied(false);

                        if (logStateChange)
                        {
                            Debug.Log($"{name}: {slot.slotId} をEmpty判定にしました。", this);
                        }
                    }
                }
            }
        }
    }

    private bool DetectCarAtSlot(ParkingSlot slot)
    {
        bool rayDetected = useRaycastDetection && DetectByRay(slot);
        bool overlapDetected = useOverlapBoxDetection && DetectByOverlapBox(slot);

        return rayDetected || overlapDetected;
    }

    private bool DetectByRay(ParkingSlot slot)
    {
        if (slot == null || slot.detectionPoint == null)
        {
            return false;
        }

        Vector3 origin = transform.position + rayOriginOffset;
        Vector3 target = slot.detectionPoint.position;
        Vector3 direction = target - origin;
        float distance = direction.magnitude;

        if (distance <= 0.01f)
        {
            return false;
        }

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

    private bool DetectByOverlapBox(ParkingSlot slot)
    {
        Transform checkTransform = slot.detectionPoint != null ? slot.detectionPoint : slot.parkingPoint;

        if (checkTransform == null)
        {
            return false;
        }

        Collider[] hits = Physics.OverlapBox(
            checkTransform.position,
            overlapBoxSize * 0.5f,
            checkTransform.rotation,
            carLayerMask
        );

        foreach (Collider hit in hits)
        {
            Car car = hit.GetComponentInParent<Car>();

            if (car == null)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugBox || targetSlots == null)
        {
            return;
        }

        Gizmos.color = new Color(0f, 1f, 1f, 0.7f);

        foreach (ParkingSlot slot in targetSlots)
        {
            if (slot == null)
            {
                continue;
            }

            Transform checkTransform = slot.detectionPoint != null ? slot.detectionPoint : slot.parkingPoint;

            if (checkTransform == null)
            {
                continue;
            }

            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(checkTransform.position, checkTransform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, overlapBoxSize);
            Gizmos.matrix = oldMatrix;
        }
    }
}
