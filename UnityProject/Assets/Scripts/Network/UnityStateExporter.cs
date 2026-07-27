using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

public class UnityStateExporter : MonoBehaviour
{
    [Header("Backend")]
    [SerializeField] private string backendSnapshotUrl = "http://localhost:8000/api/unity/snapshot";
    [SerializeField] private string sourceId = "unity-webgl-admin-01";
    [SerializeField] private float sendIntervalSeconds = 1f;

    [Header("Data Source")]
    [SerializeField] private ParkingLotManager parkingLotManager;
    [SerializeField] private bool autoFindParkingLotManager = true;
    [SerializeField] private bool includeSlots = true;
    [SerializeField] private bool includeCars = true;
    [SerializeField] private bool includeWaypoints = true;

    [Header("Debug")]
    [SerializeField] private bool logSuccess = false;
    [SerializeField] private bool logFailure = true;

    private long sequenceNumber;
    private Coroutine exportLoop;

    private void Awake()
    {
        if (parkingLotManager == null && autoFindParkingLotManager)
        {
#if UNITY_2023_1_OR_NEWER
            parkingLotManager = FindFirstObjectByType<ParkingLotManager>();
#else
            parkingLotManager = FindObjectOfType<ParkingLotManager>();
#endif
        }
    }

    private void OnEnable()
    {
        exportLoop = StartCoroutine(SendSnapshotLoop());
    }

    private void OnDisable()
    {
        if (exportLoop != null)
        {
            StopCoroutine(exportLoop);
            exportLoop = null;
        }
    }

    private IEnumerator SendSnapshotLoop()
    {
        WaitForSeconds wait = new WaitForSeconds(Mathf.Max(0.25f, sendIntervalSeconds));

        while (enabled)
        {
            yield return SendSnapshot();
            yield return wait;
        }
    }

    private IEnumerator SendSnapshot()
    {
        UnitySnapshotPayload payload = BuildPayload();
        string json = JsonUtility.ToJson(payload);
        byte[] body = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest request = new UnityWebRequest(backendSnapshotUrl, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 5;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                if (logSuccess)
                {
                    Debug.Log($"Unity snapshot sent. sequence={payload.sequenceNumber}, response={request.responseCode}");
                }
            }
            else if (logFailure)
            {
                Debug.LogWarning($"Unity snapshot failed. sequence={payload.sequenceNumber}, error={request.error}, response={request.responseCode}");
            }
        }
    }

    private UnitySnapshotPayload BuildPayload()
    {
        sequenceNumber++;

        UnitySnapshotPayload payload = new UnitySnapshotPayload
        {
            sourceId = sourceId,
            scene = SceneManager.GetActiveScene().name,
            sequenceNumber = sequenceNumber,
            timestamp = DateTimeOffset.UtcNow.ToString("o"),
            summary = BuildSummary(),
            slots = includeSlots ? BuildSlots() : new List<ParkingSlotPayload>(),
            cars = includeCars ? BuildCars() : new List<CarPayload>(),
            waypoints = includeWaypoints ? BuildWaypoints() : new List<WaypointPayload>()
        };

        return payload;
    }

    private SnapshotSummaryPayload BuildSummary()
    {
        SnapshotSummaryPayload summary = new SnapshotSummaryPayload();

        if (parkingLotManager == null)
        {
            return summary;
        }

        summary.empty = parkingLotManager.GetSlotCountByState(ParkingSlotState.Empty);
        summary.reserved = parkingLotManager.GetSlotCountByState(ParkingSlotState.Reserved);
        summary.occupied = parkingLotManager.GetSlotCountByState(ParkingSlotState.Occupied);
        summary.leaving = parkingLotManager.GetSlotCountByState(ParkingSlotState.Leaving);
        summary.disabled = parkingLotManager.GetSlotCountByState(ParkingSlotState.Disabled);
        return summary;
    }

    private List<ParkingSlotPayload> BuildSlots()
    {
        List<ParkingSlotPayload> slots = new List<ParkingSlotPayload>();

        if (parkingLotManager == null || parkingLotManager.allSlots == null)
        {
            return slots;
        }

        foreach (ParkingSlot slot in parkingLotManager.allSlots)
        {
            if (slot == null)
            {
                continue;
            }

            Transform positionSource = slot.parkingPoint != null ? slot.parkingPoint : slot.transform;
            ParkingSlotPayload item = new ParkingSlotPayload
            {
                slotId = slot.slotId,
                areaId = slot.areaId,
                state = slot.state.ToString(),
                sensorOccupied = slot.sensorOccupied,
                isLeaving = slot.isLeaving,
                position = ToVectorPayload(slot.transform.position),
                parkingPoint = ToVectorPayload(positionSource.position),
                accessWaypointId = GetWaypointId(slot.accessWaypoint),
                reservedByCarId = GetOwnerCarId(slot.reservedBy),
                occupiedByCarId = GetOwnerCarId(slot.occupiedBy)
            };

            slots.Add(item);
        }

        return slots;
    }

    private List<CarPayload> BuildCars()
    {
        List<CarPayload> cars = new List<CarPayload>();

#if UNITY_2023_1_OR_NEWER
        Car[] carObjects = FindObjectsByType<Car>(FindObjectsSortMode.None);
#else
        Car[] carObjects = FindObjectsOfType<Car>();
#endif

        foreach (Car car in carObjects)
        {
            if (car == null)
            {
                continue;
            }

            NPC_CarController controller = car.GetComponent<NPC_CarController>();
            ParkingSlot targetSlot = controller != null ? controller.targetParkingSlot : car.currentParkingSlot;

            CarPayload item = new CarPayload
            {
                carId = car.carId,
                state = controller != null ? controller.moveState.ToString() : (car.isParked ? "Parked" : "Unknown"),
                position = ToVectorPayload(car.transform.position),
                targetSlotId = targetSlot != null ? targetSlot.slotId : null,
                targetAreaId = targetSlot != null ? targetSlot.areaId : null,
                isStoppedByFrontCar = controller != null && controller.isStoppedByFrontCar
            };

            cars.Add(item);
        }

        return cars;
    }

    private List<WaypointPayload> BuildWaypoints()
    {
        List<WaypointPayload> waypoints = new List<WaypointPayload>();

#if UNITY_2023_1_OR_NEWER
        Waypoint[] waypointObjects = FindObjectsByType<Waypoint>(FindObjectsSortMode.None);
#else
        Waypoint[] waypointObjects = FindObjectsOfType<Waypoint>();
#endif

        foreach (Waypoint waypoint in waypointObjects)
        {
            if (waypoint == null)
            {
                continue;
            }

            WaypointPayload item = new WaypointPayload
            {
                waypointId = GetWaypointId(waypoint),
                name = waypoint.name,
                position = ToVectorPayload(waypoint.transform.position),
                nextWaypointIds = new List<string>()
            };

            if (waypoint.nextWaypoints != null)
            {
                foreach (Waypoint next in waypoint.nextWaypoints)
                {
                    string nextId = GetWaypointId(next);
                    if (!string.IsNullOrEmpty(nextId))
                    {
                        item.nextWaypointIds.Add(nextId);
                    }
                }
            }

            waypoints.Add(item);
        }

        return waypoints;
    }

    private string GetOwnerCarId(NPC_CarController owner)
    {
        if (owner == null)
        {
            return null;
        }

        Car car = owner.GetComponent<Car>();
        return car != null ? car.carId : owner.name;
    }

    private string GetWaypointId(Waypoint waypoint)
    {
        if (waypoint == null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(waypoint.waypointId) ? waypoint.name : waypoint.waypointId;
    }

    private Vector3Payload ToVectorPayload(Vector3 value)
    {
        return new Vector3Payload { x = value.x, y = value.y, z = value.z };
    }
}

[Serializable]
public class UnitySnapshotPayload
{
    public string sourceId;
    public string scene;
    public long sequenceNumber;
    public string timestamp;
    public SnapshotSummaryPayload summary;
    public List<ParkingSlotPayload> slots;
    public List<CarPayload> cars;
    public List<WaypointPayload> waypoints;
}

[Serializable]
public class SnapshotSummaryPayload
{
    public int empty;
    public int reserved;
    public int occupied;
    public int leaving;
    public int disabled;
}

[Serializable]
public class ParkingSlotPayload
{
    public string slotId;
    public string areaId;
    public string state;
    public bool sensorOccupied;
    public bool isLeaving;
    public Vector3Payload position;
    public Vector3Payload parkingPoint;
    public string accessWaypointId;
    public string reservedByCarId;
    public string occupiedByCarId;
}

[Serializable]
public class CarPayload
{
    public string carId;
    public string state;
    public Vector3Payload position;
    public string targetSlotId;
    public string targetAreaId;
    public bool isStoppedByFrontCar;
}

[Serializable]
public class WaypointPayload
{
    public string waypointId;
    public string name;
    public Vector3Payload position;
    public List<string> nextWaypointIds;
}

[Serializable]
public class Vector3Payload
{
    public float x;
    public float y;
    public float z;
}