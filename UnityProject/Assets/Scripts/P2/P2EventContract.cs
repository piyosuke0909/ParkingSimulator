using System;
using System.Collections.Generic;

public static class P2EventContractConstants
{
    public const string ContractName = "smart-parking.simulation-event";
    public const string SchemaVersion = "1.0";
    public const string SourceSystem = "unity";

    public const string ScenarioStarted = "scenario.started";
    public const string ScenarioCompleted = "scenario.completed";
    public const string ScenarioFactorActivated = "scenario_factor.activated";
    public const string ScenarioFactorDeactivated = "scenario_factor.deactivated";
    public const string EntranceSelected = "entrance.selected";
    public const string ParkingAreaSelected = "parking_area.selected";
    public const string ParkingSlotSelected = "parking_slot.selected";
    public const string VehicleSpawned = "vehicle.spawned";
    public const string VehicleParked = "vehicle.parked";
    public const string VehicleLeaving = "vehicle.leaving";
    public const string VehicleExited = "vehicle.exited";
    public const string CommandAccepted = "command.accepted";
    public const string CommandStarted = "command.started";
    public const string CommandSucceeded = "command.succeeded";
    public const string CommandFailed = "command.failed";
    public const string CommandRejected = "command.rejected";
    public const string CommandExpired = "command.expired";
}

[Serializable]
public class P2SimulationEvent
{
    public string contractName = P2EventContractConstants.ContractName;
    public string schemaVersion = P2EventContractConstants.SchemaVersion;
    public string eventId;
    public string eventType;
    public string sourceSystem = P2EventContractConstants.SourceSystem;
    public string generatedAtUtc;
    public string sceneName;
    public string sessionId;
    public string runId;
    public long sequenceNumber;
    public string scenarioId;
    public string scenarioVersion;
    public string facilityId;
    public double simulationTimeSeconds;
    public string correlationId;
    public string commandId;
    public string entityType;
    public string entityId;
    public P2EventPayload payload = new P2EventPayload();
}

[Serializable]
public class P2EventPayload
{
    public P2ScenarioEventPayload scenario;
    public P2ScenarioFactorEventPayload scenarioFactor;
    public P2SelectionEventPayload selection;
    public P2VehicleEventPayload vehicle;
    public P2CommandEventPayload command;
}

[Serializable]
public class P2ScenarioEventPayload
{
    public string startReason;
    public string completionReason;
    public string scenarioName;
    public string mapVersion;
    public string timeOfDay;
    public int randomSeed;
    public string randomSeedPolicy;
    public double durationSeconds;
    public int activeVehicleCount;
    public int totalSpawnedCount;
}

[Serializable]
public class P2ScenarioFactorEventPayload
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
    public List<P2EventFactorEffect> effects = new List<P2EventFactorEffect>();
}

[Serializable]
public class P2EventFactorEffect
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
public class P2SelectionEventPayload
{
    public string selectionType;
    public string selectedCanonicalId;
    public string selectedSceneId;
    public string selectedParentCanonicalId;
    public string selectedParentSceneId;
    public string selectedSlotId;
    public int selectedAvailableSlotCount;
    public List<P2WeightedSelectionCandidate> candidates = new List<P2WeightedSelectionCandidate>();
}

[Serializable]
public class P2WeightedSelectionCandidate
{
    public string canonicalId;
    public string sceneId;
    public double weight;
    public int availableSlotCount;
}

[Serializable]
public class P2VehicleEventPayload
{
    public string vehicleId;
    public string objectName;
    public int spawnSequenceNumber;
    public double spawnedAtSimulationTimeSeconds;
    public string entranceAccessPointId;
    public string entranceScenePairName;
    public string targetAreaId;
    public string targetSceneAreaId;
    public string targetSlotId;
    public string currentSlotId;
    public string movementState;
    public string previousMovementState;
    public string reason;
    public int activeVehicleCount;
    public int maxConcurrentVehicles;
    public int totalSpawnedCount;
    public P2Vector3Snapshot position = new P2Vector3Snapshot();
}


[Serializable]
public class P2CommandEventPayload
{
    public string idempotencyKey;
    public string commandType;
    public string status;
    public string reasonCode;
    public string message;
    public bool retryable;
    public P2CommandResultPayload result = new P2CommandResultPayload();
}

[Serializable]
public class P2CommandResultPayload
{
    public string areaId;
    public string previousPolicy;
    public string appliedPolicy;
}
