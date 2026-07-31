using System;
using System.Collections.Generic;

public static class P2SnapshotContractConstants
{
    public const string ContractName = "smart-parking.simulation-snapshot";
    public const string SchemaVersion = "1.1";
    public const string SnapshotType = "simulation_snapshot";
    public const string SourceSystem = "unity";
}

[Serializable]
public class P2SimulationSnapshot
{
    public string contractName = P2SnapshotContractConstants.ContractName;
    public string schemaVersion = P2SnapshotContractConstants.SchemaVersion;
    public string snapshotType = P2SnapshotContractConstants.SnapshotType;
    public string snapshotId;
    public string sourceSystem = P2SnapshotContractConstants.SourceSystem;
    public string generatedAtUtc;
    public string sceneName;
    public string sessionId;
    public string runId;
    public long sequenceNumber;

    public P2ScenarioSnapshot scenario = new P2ScenarioSnapshot();
    public P2RuntimeSnapshot runtime = new P2RuntimeSnapshot();
    public P2SnapshotSummary summary = new P2SnapshotSummary();

    public List<P2ScenarioFactorSnapshot> activeScenarioFactors = new List<P2ScenarioFactorSnapshot>();
    public List<P2AccessPointSnapshot> accessPoints = new List<P2AccessPointSnapshot>();
    public List<P2AreaSnapshot> areas = new List<P2AreaSnapshot>();
    public List<P2ParkingSlotSnapshot> parkingSlots = new List<P2ParkingSlotSnapshot>();
    public List<P2VehicleSnapshot> vehicles = new List<P2VehicleSnapshot>();
}

[Serializable]
public class P2ScenarioSnapshot
{
    public string scenarioId;
    public string scenarioVersion;
    public string scenarioName;
    public string facilityId;
    public string mapVersion;
    public string timeOfDay;
    public int randomSeed;
    public string randomSeedPolicy;
    public double simulationTimeSeconds;
    public double durationSeconds;
    public bool isCompleted;
}

[Serializable]
public class P2RuntimeSnapshot
{
    public string arrivalRateSourceId;
    public string arrivalDistribution;
    public double baseVehiclesPerMinute;
    public double arrivalRateMultiplier = 1d;
    public double effectiveVehiclesPerMinute;
    public double vehicleSpeedMultiplier = 1d;
    public int activeVehicleCount;
    public int discoveredVehicleCount;
    public int maxConcurrentVehicles;
    public int totalSpawnedCount;
}

[Serializable]
public class P2SnapshotSummary
{
    public int totalSlotCount;
    public int availableSlotCount;
    public int emptySlotCount;
    public int reservedSlotCount;
    public int occupiedSlotCount;
    public int leavingSlotCount;
    public int disabledSlotCount;
    public int areaCount;
    public int accessPointCount;
    public int vehicleCount;
    public int activeScenarioFactorCount;
}

[Serializable]
public class P2ScenarioFactorSnapshot
{
    public string scenarioFactorId;
    public string factorCode;
    public string label;
    public string category;
    public string informationAvailability;
    public double intensity;
    public double startSimulationTimeSeconds;
    public double endSimulationTimeSeconds;
    public bool hasKnownFromSimulationTimeSeconds;
    public double knownFromSimulationTimeSeconds;
    public List<P2ScenarioFactorEffectSnapshot> effects = new List<P2ScenarioFactorEffectSnapshot>();
}

[Serializable]
public class P2ScenarioFactorEffectSnapshot
{
    public string scenarioFactorEffectId;
    public string targetType;
    public string targetId;
    public string resolvedTargetId;
    public string metric;
    public string operation;
    public double value;
    public string unit;
}

[Serializable]
public class P2AccessPointSnapshot
{
    public string accessPointId;
    public string scenePairName;
    public string entranceWaypointId;
    public string exitWaypointId;
    public double currentPreferenceWeight = 1d;
    public P2Vector3Snapshot spawnPosition = new P2Vector3Snapshot();
}

[Serializable]
public class P2AreaSnapshot
{
    public string areaId;
    public string sceneAreaId;
    public int totalSlotCount;
    public int availableSlotCount;
    public int emptySlotCount;
    public int reservedSlotCount;
    public int occupiedSlotCount;
    public int leavingSlotCount;
    public int disabledSlotCount;
    public double currentPreferenceWeight = 1d;
}

[Serializable]
public class P2ParkingSlotSnapshot
{
    public string slotId;
    public string areaId;
    public string sceneAreaId;
    public string state;
    public bool isAvailable;
    public bool sensorOccupied;
    public bool isLeaving;
    public string reservedByVehicleId;
    public string occupiedByVehicleId;
    public string accessWaypointId;
    public P2Vector3Snapshot position = new P2Vector3Snapshot();
    public P2Vector3Snapshot parkingPointPosition = new P2Vector3Snapshot();
}

[Serializable]
public class P2VehicleSnapshot
{
    public string vehicleId;
    public string objectName;
    public string movementState;
    public bool isParked;
    public bool isStopped;
    public bool isStoppedByFrontVehicle;
    public string stopReason;

    public string entranceAccessPointId;
    public string entranceScenePairName;
    public string targetAreaId;
    public string targetSceneAreaId;
    public string targetSlotId;
    public string currentAreaId;
    public string currentSceneAreaId;
    public string currentSlotId;
    public string currentTargetWaypointId;

    public int spawnSequenceNumber;
    public double spawnedAtSimulationTimeSeconds;
    public double currentSpeedMetersPerSecond;

    public P2Vector3Snapshot position = new P2Vector3Snapshot();
    public P2Vector3Snapshot rotationEuler = new P2Vector3Snapshot();
    public P2Vector3Snapshot velocity = new P2Vector3Snapshot();
}

[Serializable]
public class P2Vector3Snapshot
{
    public double x;
    public double y;
    public double z;
}
