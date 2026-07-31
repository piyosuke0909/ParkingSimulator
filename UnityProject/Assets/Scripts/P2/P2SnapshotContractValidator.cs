using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class P2SnapshotValidationResult
{
    public bool isValid;
    public List<string> errors = new List<string>();
    public List<string> warnings = new List<string>();
}

public class P2SnapshotContractValidator : MonoBehaviour
{
    [Header("Validation")]
    public bool requireVehicles = false;
    public bool requireParkingSlots = true;
    public bool requireAreas = true;
    public bool requireAccessPoints = true;
    public bool warnWhenActiveCountDiffersFromDiscoveredCount = true;

    public P2SnapshotValidationResult Validate(P2SimulationSnapshot snapshot)
    {
        P2SnapshotValidationResult result = new P2SnapshotValidationResult();

        if (snapshot == null)
        {
            result.errors.Add("Snapshot is null.");
            result.isValid = false;
            return result;
        }

        RequireText(result, snapshot.contractName, "contractName");
        RequireText(result, snapshot.schemaVersion, "schemaVersion");
        RequireText(result, snapshot.snapshotType, "snapshotType");
        RequireText(result, snapshot.snapshotId, "snapshotId");
        RequireText(result, snapshot.sourceSystem, "sourceSystem");
        RequireText(result, snapshot.generatedAtUtc, "generatedAtUtc");
        RequireText(result, snapshot.sceneName, "sceneName");
        RequireText(result, snapshot.sessionId, "sessionId");
        RequireText(result, snapshot.runId, "runId");

        if (!string.IsNullOrWhiteSpace(snapshot.sessionId) &&
            !string.IsNullOrWhiteSpace(snapshot.runId) &&
            !snapshot.runId.StartsWith(
                snapshot.sessionId + "-run-",
                StringComparison.Ordinal))
        {
            result.errors.Add("runId must belong to sessionId.");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.snapshotId) &&
            !string.IsNullOrWhiteSpace(snapshot.runId) &&
            snapshot.snapshotId.IndexOf(snapshot.runId, StringComparison.Ordinal) < 0)
        {
            result.errors.Add("snapshotId must include runId.");
        }

        if (snapshot.sequenceNumber <= 0)
        {
            result.errors.Add("sequenceNumber must be greater than 0.");
        }

        ValidateScenario(result, snapshot.scenario);
        ValidateRuntime(result, snapshot.runtime);
        ValidateCollections(result, snapshot);
        ValidateSummary(result, snapshot);

        result.isValid = result.errors.Count == 0;
        return result;
    }

    private void ValidateScenario(
        P2SnapshotValidationResult result,
        P2ScenarioSnapshot scenario)
    {
        if (scenario == null)
        {
            result.errors.Add("scenario is required.");
            return;
        }

        RequireText(result, scenario.scenarioId, "scenario.scenarioId");
        RequireText(result, scenario.facilityId, "scenario.facilityId");

        if (scenario.simulationTimeSeconds < 0d)
        {
            result.errors.Add("scenario.simulationTimeSeconds must not be negative.");
        }

        if (scenario.durationSeconds < 0d)
        {
            result.errors.Add("scenario.durationSeconds must not be negative.");
        }
    }

    private void ValidateRuntime(
        P2SnapshotValidationResult result,
        P2RuntimeSnapshot runtime)
    {
        if (runtime == null)
        {
            result.errors.Add("runtime is required.");
            return;
        }

        RequireText(result, runtime.arrivalRateSourceId, "runtime.arrivalRateSourceId");
        RequireText(result, runtime.arrivalDistribution, "runtime.arrivalDistribution");

        if (runtime.baseVehiclesPerMinute < 0d)
        {
            result.errors.Add("runtime.baseVehiclesPerMinute must not be negative.");
        }

        if (runtime.arrivalRateMultiplier < 0d)
        {
            result.errors.Add("runtime.arrivalRateMultiplier must not be negative.");
        }

        if (runtime.effectiveVehiclesPerMinute < 0d)
        {
            result.errors.Add("runtime.effectiveVehiclesPerMinute must not be negative.");
        }

        if (runtime.vehicleSpeedMultiplier < 0d)
        {
            result.errors.Add("runtime.vehicleSpeedMultiplier must not be negative.");
        }

        if (runtime.activeVehicleCount < 0 ||
            runtime.discoveredVehicleCount < 0 ||
            runtime.maxConcurrentVehicles < 0 ||
            runtime.totalSpawnedCount < 0)
        {
            result.errors.Add("runtime vehicle counts must not be negative.");
        }

        if (runtime.maxConcurrentVehicles > 0 &&
            runtime.activeVehicleCount > runtime.maxConcurrentVehicles)
        {
            result.errors.Add(
                "runtime.activeVehicleCount exceeds runtime.maxConcurrentVehicles."
            );
        }

        if (warnWhenActiveCountDiffersFromDiscoveredCount &&
            runtime.activeVehicleCount != runtime.discoveredVehicleCount)
        {
            result.warnings.Add(
                "runtime.activeVehicleCount differs from runtime.discoveredVehicleCount. " +
                "The active vehicle set may be awaiting an explicit cleanup or may exclude non-managed vehicles."
            );
        }
    }


    private void ValidateSummary(
        P2SnapshotValidationResult result,
        P2SimulationSnapshot snapshot)
    {
        if (snapshot.summary == null)
        {
            result.errors.Add("summary is required.");
            return;
        }

        if (snapshot.parkingSlots != null &&
            snapshot.summary.totalSlotCount != snapshot.parkingSlots.Count)
        {
            result.errors.Add(
                "summary.totalSlotCount does not match parkingSlots.Count."
            );
        }

        if (snapshot.areas != null &&
            snapshot.summary.areaCount != snapshot.areas.Count)
        {
            result.errors.Add("summary.areaCount does not match areas.Count.");
        }

        if (snapshot.accessPoints != null &&
            snapshot.summary.accessPointCount != snapshot.accessPoints.Count)
        {
            result.errors.Add(
                "summary.accessPointCount does not match accessPoints.Count."
            );
        }

        if (snapshot.vehicles != null &&
            snapshot.summary.vehicleCount != snapshot.vehicles.Count)
        {
            result.errors.Add("summary.vehicleCount does not match vehicles.Count.");
        }

        if (snapshot.activeScenarioFactors != null &&
            snapshot.summary.activeScenarioFactorCount != snapshot.activeScenarioFactors.Count)
        {
            result.errors.Add(
                "summary.activeScenarioFactorCount does not match activeScenarioFactors.Count."
            );
        }

        int categorizedSlotCount =
            snapshot.summary.emptySlotCount +
            snapshot.summary.reservedSlotCount +
            snapshot.summary.occupiedSlotCount +
            snapshot.summary.leavingSlotCount +
            snapshot.summary.disabledSlotCount;

        if (categorizedSlotCount != snapshot.summary.totalSlotCount)
        {
            result.errors.Add(
                "The sum of slot state counts does not match summary.totalSlotCount."
            );
        }

        if (snapshot.summary.availableSlotCount < 0 ||
            snapshot.summary.availableSlotCount > snapshot.summary.totalSlotCount)
        {
            result.errors.Add(
                "summary.availableSlotCount must be between 0 and totalSlotCount."
            );
        }
    }

    private void ValidateCollections(
        P2SnapshotValidationResult result,
        P2SimulationSnapshot snapshot)
    {
        if (requireParkingSlots &&
            (snapshot.parkingSlots == null || snapshot.parkingSlots.Count == 0))
        {
            result.errors.Add("parkingSlots must contain at least one item.");
        }

        if (requireAreas &&
            (snapshot.areas == null || snapshot.areas.Count == 0))
        {
            result.errors.Add("areas must contain at least one item.");
        }

        if (requireAccessPoints &&
            (snapshot.accessPoints == null || snapshot.accessPoints.Count == 0))
        {
            result.errors.Add("accessPoints must contain at least one item.");
        }

        if (requireVehicles &&
            (snapshot.vehicles == null || snapshot.vehicles.Count == 0))
        {
            result.errors.Add("vehicles must contain at least one item.");
        }

        HashSet<string> areaIds = ValidateAreaIds(result, snapshot.areas);
        HashSet<string> accessPointIds = ValidateAccessPointIds(result, snapshot.accessPoints);
        HashSet<string> slotIds = ValidateSlotIds(result, snapshot.parkingSlots, areaIds);
        ValidateVehicleIds(result, snapshot.vehicles, slotIds, areaIds);
        ValidateFactorIds(
            result,
            snapshot.activeScenarioFactors,
            areaIds,
            accessPointIds
        );
    }

    private HashSet<string> ValidateAreaIds(
        P2SnapshotValidationResult result,
        List<P2AreaSnapshot> areas)
    {
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

        if (areas == null)
        {
            return ids;
        }

        foreach (P2AreaSnapshot area in areas)
        {
            if (area == null)
            {
                result.errors.Add("areas contains a null item.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(area.areaId))
            {
                result.errors.Add("areas contains an item without areaId.");
                continue;
            }

            if (!ids.Add(area.areaId))
            {
                result.errors.Add("Duplicate areaId: " + area.areaId);
            }

            if (area.totalSlotCount < 0 || area.availableSlotCount < 0)
            {
                result.errors.Add("Area counts must not be negative: " + area.areaId);
            }
        }

        return ids;
    }

    private HashSet<string> ValidateSlotIds(
        P2SnapshotValidationResult result,
        List<P2ParkingSlotSnapshot> slots,
        HashSet<string> areaIds)
    {
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

        if (slots == null)
        {
            return ids;
        }

        foreach (P2ParkingSlotSnapshot slot in slots)
        {
            if (slot == null)
            {
                result.errors.Add("parkingSlots contains a null item.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(slot.slotId))
            {
                result.errors.Add("parkingSlots contains an item without slotId.");
                continue;
            }

            if (!ids.Add(slot.slotId))
            {
                result.errors.Add("Duplicate slotId: " + slot.slotId);
            }

            if (string.IsNullOrWhiteSpace(slot.areaId))
            {
                result.errors.Add("parkingSlot has no areaId: " + slot.slotId);
            }
            else if (areaIds.Count > 0 && !areaIds.Contains(slot.areaId))
            {
                result.errors.Add(
                    "parkingSlot references an unknown areaId: " +
                    slot.slotId + " -> " + slot.areaId
                );
            }
        }

        return ids;
    }

    private void ValidateVehicleIds(
        P2SnapshotValidationResult result,
        List<P2VehicleSnapshot> vehicles,
        HashSet<string> slotIds,
        HashSet<string> areaIds)
    {
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

        if (vehicles == null)
        {
            return;
        }

        foreach (P2VehicleSnapshot vehicle in vehicles)
        {
            if (vehicle == null)
            {
                result.errors.Add("vehicles contains a null item.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(vehicle.vehicleId))
            {
                result.errors.Add("vehicles contains an item without vehicleId.");
                continue;
            }

            if (!ids.Add(vehicle.vehicleId))
            {
                result.errors.Add("Duplicate vehicleId: " + vehicle.vehicleId);
            }

            ValidateOptionalReference(
                result,
                vehicle.vehicleId,
                "targetSlotId",
                vehicle.targetSlotId,
                slotIds
            );
            ValidateOptionalReference(
                result,
                vehicle.vehicleId,
                "currentSlotId",
                vehicle.currentSlotId,
                slotIds
            );
            ValidateOptionalReference(
                result,
                vehicle.vehicleId,
                "targetAreaId",
                vehicle.targetAreaId,
                areaIds
            );
            ValidateOptionalReference(
                result,
                vehicle.vehicleId,
                "currentAreaId",
                vehicle.currentAreaId,
                areaIds
            );
        }
    }

    private HashSet<string> ValidateAccessPointIds(
        P2SnapshotValidationResult result,
        List<P2AccessPointSnapshot> accessPoints)
    {
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

        if (accessPoints == null)
        {
            return ids;
        }

        foreach (P2AccessPointSnapshot accessPoint in accessPoints)
        {
            if (accessPoint == null)
            {
                result.errors.Add("accessPoints contains a null item.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(accessPoint.accessPointId))
            {
                result.errors.Add("accessPoints contains an item without accessPointId.");
                continue;
            }

            if (!ids.Add(accessPoint.accessPointId))
            {
                result.errors.Add("Duplicate accessPointId: " + accessPoint.accessPointId);
            }

            if (accessPoint.currentPreferenceWeight < 0d)
            {
                result.errors.Add(
                    "Access point currentPreferenceWeight must not be negative: " +
                    accessPoint.accessPointId
                );
            }
        }

        return ids;
    }

    private void ValidateFactorIds(
        P2SnapshotValidationResult result,
        List<P2ScenarioFactorSnapshot> factors,
        HashSet<string> areaIds,
        HashSet<string> accessPointIds)
    {
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

        if (factors == null)
        {
            return;
        }

        foreach (P2ScenarioFactorSnapshot factor in factors)
        {
            if (factor == null)
            {
                result.errors.Add("activeScenarioFactors contains a null item.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(factor.scenarioFactorId))
            {
                result.errors.Add("activeScenarioFactors contains an item without scenarioFactorId.");
                continue;
            }

            if (!ids.Add(factor.scenarioFactorId))
            {
                result.errors.Add("Duplicate scenarioFactorId: " + factor.scenarioFactorId);
            }

            ValidateFactorEffects(
                result,
                factor,
                areaIds,
                accessPointIds
            );
        }
    }

    private static void ValidateFactorEffects(
        P2SnapshotValidationResult result,
        P2ScenarioFactorSnapshot factor,
        HashSet<string> areaIds,
        HashSet<string> accessPointIds)
    {
        if (factor.effects == null)
        {
            return;
        }

        foreach (P2ScenarioFactorEffectSnapshot effect in factor.effects)
        {
            if (effect == null)
            {
                result.errors.Add(
                    "Scenario factor contains a null effect: " + factor.scenarioFactorId
                );
                continue;
            }

            if (string.IsNullOrWhiteSpace(effect.targetId))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(effect.resolvedTargetId))
            {
                result.errors.Add(
                    "Effect resolvedTargetId is required when targetId is set: " +
                    factor.scenarioFactorId + " / " + effect.scenarioFactorEffectId
                );
                continue;
            }

            if (string.Equals(effect.targetType, "AccessPoint", StringComparison.Ordinal) &&
                accessPointIds != null &&
                accessPointIds.Count > 0 &&
                !accessPointIds.Contains(effect.resolvedTargetId))
            {
                result.warnings.Add(
                    "Effect resolvedTargetId is not included in accessPoints: " +
                    effect.resolvedTargetId
                );
            }
            else if (string.Equals(effect.targetType, "Area", StringComparison.Ordinal) &&
                     areaIds != null &&
                     areaIds.Count > 0 &&
                     !areaIds.Contains(effect.resolvedTargetId))
            {
                result.warnings.Add(
                    "Effect resolvedTargetId is not included in areas: " +
                    effect.resolvedTargetId
                );
            }
        }
    }

    private static void ValidateOptionalReference(
        P2SnapshotValidationResult result,
        string ownerId,
        string fieldName,
        string referenceId,
        HashSet<string> knownIds)
    {
        if (string.IsNullOrWhiteSpace(referenceId) || knownIds == null || knownIds.Count == 0)
        {
            return;
        }

        if (!knownIds.Contains(referenceId))
        {
            result.warnings.Add(
                ownerId + "." + fieldName + " references an ID not included in this Snapshot: " +
                referenceId
            );
        }
    }

    private static void RequireText(
        P2SnapshotValidationResult result,
        string value,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result.errors.Add(fieldName + " is required.");
        }
    }
}
