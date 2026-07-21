using System;
using System.Collections.Generic;
using UnityEngine;

public enum ScenarioFactorCategory
{
    Weather,
    Temperature,
    ScheduledEvent,
    Traffic,
    FacilityOperation
}

public enum InformationAvailability
{
    Scheduled,
    Forecasted,
    ObservedOnly
}

public enum FactorEffectTargetType
{
    Facility,
    Area,
    AccessPoint,
    DriverProfile,
    VehiclePopulation,
    WaypointEdge
}

public enum FactorEffectOperation
{
    Add,
    Multiply,
    Set
}

public enum ArrivalDistribution
{
    Poisson,
    Fixed,
    Custom
}

public static class ScenarioMetrics
{
    public const string ArrivalRate = "arrival_rate";
    public const string Speed = "speed";
    public const string ParkingPreferenceWeight = "parking_preference_weight";
}

[Serializable]
public class ArrivalRateSegment
{
    public bool enabled = true;
    public string arrivalRateSegmentId = "arrival-base";

    [Tooltip("空欄の場合は施設全体に適用します。")]
    public string accessPointId;

    [Min(0f)]
    public float startSimulationTimeSeconds;

    [Min(0f)]
    public float endSimulationTimeSeconds = 14400f;

    [Min(0f)]
    public float vehiclesPerMinute = 12f;

    public ArrivalDistribution distribution = ArrivalDistribution.Fixed;

    public bool IsActiveAt(float simulationTimeSeconds)
    {
        return enabled &&
               simulationTimeSeconds >= startSimulationTimeSeconds &&
               simulationTimeSeconds < endSimulationTimeSeconds;
    }
}

[Serializable]
public class ScenarioFactorEffect
{
    public bool enabled = true;
    public string scenarioFactorEffectId;
    public FactorEffectTargetType targetType = FactorEffectTargetType.Facility;

    [Tooltip("施設全体・車両母集団など対象IDが不要な場合は空欄にします。")]
    public string targetId;

    [Tooltip("arrival_rate / speed / parking_preference_weight など契約上のmetric名です。")]
    public string metric;

    public FactorEffectOperation operation = FactorEffectOperation.Multiply;
    public float value = 1f;
    public string unit = "ratio";
}

[Serializable]
public class ScenarioFactorDefinition
{
    public bool enabled = true;
    public string scenarioFactorId;
    public ScenarioFactorCategory category;
    public string factorCode;
    public string label;

    [Min(0f)]
    public float startSimulationTimeSeconds;

    [Min(0f)]
    public float endSimulationTimeSeconds;

    [Min(0f)]
    public float intensity = 1f;

    public InformationAvailability informationAvailability = InformationAvailability.Scheduled;

    [Tooltip("null相当を表現するためのフラグです。OFFの場合knownFromSimulationTimeSecondsは使用しません。")]
    public bool hasKnownFromSimulationTimeSeconds = true;

    [Min(0f)]
    public float knownFromSimulationTimeSeconds;

    public List<ScenarioFactorEffect> effects = new List<ScenarioFactorEffect>();

    public bool IsActiveAt(float simulationTimeSeconds)
    {
        return enabled &&
               simulationTimeSeconds >= startSimulationTimeSeconds &&
               simulationTimeSeconds < endSimulationTimeSeconds;
    }
}
