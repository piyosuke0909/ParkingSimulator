using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

[Serializable]
public class P2EventValidationResult
{
    public bool isValid = true;
    public List<string> errors = new List<string>();
    public List<string> warnings = new List<string>();

    public void AddError(string message)
    {
        isValid = false;
        errors.Add(message ?? string.Empty);
    }

    public void AddWarning(string message)
    {
        warnings.Add(message ?? string.Empty);
    }
}

public class P2EventContractValidator : MonoBehaviour
{
    [Header("Validation")]
    public bool requireKnownEventType = true;
    public bool requirePayloadForTypedEvents = true;

    private static readonly HashSet<string> KnownEventTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        P2EventContractConstants.ScenarioStarted,
        P2EventContractConstants.ScenarioCompleted,
        P2EventContractConstants.ScenarioFactorActivated,
        P2EventContractConstants.ScenarioFactorDeactivated,
        P2EventContractConstants.EntranceSelected,
        P2EventContractConstants.ParkingAreaSelected,
        P2EventContractConstants.ParkingSlotSelected,
        P2EventContractConstants.VehicleSpawned,
        P2EventContractConstants.VehicleParked,
        P2EventContractConstants.VehicleLeaving,
        P2EventContractConstants.VehicleExited,
        P2EventContractConstants.CommandAccepted,
        P2EventContractConstants.CommandStarted,
        P2EventContractConstants.CommandSucceeded,
        P2EventContractConstants.CommandFailed,
        P2EventContractConstants.CommandRejected,
        P2EventContractConstants.CommandExpired
    };

    public P2EventValidationResult Validate(P2SimulationEvent simulationEvent)
    {
        P2EventValidationResult result = new P2EventValidationResult();

        if (simulationEvent == null)
        {
            result.AddError("Event is null.");
            return result;
        }

        RequireText(result, simulationEvent.contractName, "contractName");
        RequireText(result, simulationEvent.schemaVersion, "schemaVersion");
        RequireText(result, simulationEvent.eventId, "eventId");
        RequireText(result, simulationEvent.eventType, "eventType");
        RequireText(result, simulationEvent.generatedAtUtc, "generatedAtUtc");

        DateTimeOffset generatedAtUtc;
        if (!string.IsNullOrWhiteSpace(simulationEvent.generatedAtUtc) &&
            !DateTimeOffset.TryParse(
                simulationEvent.generatedAtUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out generatedAtUtc
            ))
        {
            result.AddError("generatedAtUtc must be a valid date-time string.");
        }

        if (simulationEvent.commandId != null &&
            simulationEvent.commandId.Length > 0 &&
            string.IsNullOrWhiteSpace(simulationEvent.commandId))
        {
            result.AddError("commandId must not contain only whitespace when supplied.");
        }
        RequireText(result, simulationEvent.sceneName, "sceneName");
        RequireText(result, simulationEvent.sessionId, "sessionId");
        RequireText(result, simulationEvent.runId, "runId");
        RequireText(result, simulationEvent.scenarioId, "scenarioId");

        if (!string.Equals(
                simulationEvent.contractName,
                P2EventContractConstants.ContractName,
                StringComparison.Ordinal))
        {
            result.AddError("contractName does not match the P2 Event contract.");
        }

        if (!string.Equals(
                simulationEvent.schemaVersion,
                P2EventContractConstants.SchemaVersion,
                StringComparison.Ordinal))
        {
            result.AddError("schemaVersion does not match the supported Event schema.");
        }

        if (simulationEvent.sequenceNumber <= 0L)
        {
            result.AddError("sequenceNumber must be greater than zero.");
        }

        if (!IsFiniteNonNegative(simulationEvent.simulationTimeSeconds))
        {
            result.AddError("simulationTimeSeconds must be finite and non-negative.");
        }

        if (requireKnownEventType && !KnownEventTypes.Contains(simulationEvent.eventType ?? string.Empty))
        {
            result.AddError("Unknown eventType: " + (simulationEvent.eventType ?? string.Empty));
        }

        ValidateTypedPayload(simulationEvent, result);
        return result;
    }

    private void ValidateTypedPayload(
        P2SimulationEvent simulationEvent,
        P2EventValidationResult result)
    {
        if (!requirePayloadForTypedEvents)
        {
            return;
        }

        P2EventPayload payload = simulationEvent.payload;

        if (payload == null)
        {
            result.AddError("payload is required.");
            return;
        }

        string eventType = simulationEvent.eventType ?? string.Empty;

        if (eventType == P2EventContractConstants.ScenarioStarted ||
            eventType == P2EventContractConstants.ScenarioCompleted)
        {
            if (payload.scenario == null)
            {
                result.AddError("payload.scenario is required for " + eventType + ".");
            }

            return;
        }

        if (eventType == P2EventContractConstants.ScenarioFactorActivated ||
            eventType == P2EventContractConstants.ScenarioFactorDeactivated)
        {
            if (payload.scenarioFactor == null)
            {
                result.AddError("payload.scenarioFactor is required for " + eventType + ".");
                return;
            }

            RequireText(
                result,
                payload.scenarioFactor.scenarioFactorId,
                "payload.scenarioFactor.scenarioFactorId"
            );
            return;
        }

        if (eventType == P2EventContractConstants.EntranceSelected ||
            eventType == P2EventContractConstants.ParkingAreaSelected ||
            eventType == P2EventContractConstants.ParkingSlotSelected)
        {
            if (payload.selection == null)
            {
                result.AddError("payload.selection is required for " + eventType + ".");
                return;
            }

            RequireText(
                result,
                payload.selection.selectedCanonicalId,
                "payload.selection.selectedCanonicalId"
            );
            return;
        }

        if (eventType == P2EventContractConstants.VehicleSpawned ||
            eventType == P2EventContractConstants.VehicleParked ||
            eventType == P2EventContractConstants.VehicleLeaving ||
            eventType == P2EventContractConstants.VehicleExited)
        {
            if (payload.vehicle == null)
            {
                result.AddError("payload.vehicle is required for " + eventType + ".");
                return;
            }

            RequireText(result, payload.vehicle.vehicleId, "payload.vehicle.vehicleId");
        }

        if (eventType == P2EventContractConstants.CommandAccepted ||
            eventType == P2EventContractConstants.CommandStarted ||
            eventType == P2EventContractConstants.CommandSucceeded ||
            eventType == P2EventContractConstants.CommandFailed ||
            eventType == P2EventContractConstants.CommandRejected ||
            eventType == P2EventContractConstants.CommandExpired)
        {
            RequireText(result, simulationEvent.commandId, "commandId");

            if (payload.command == null)
            {
                result.AddError("payload.command is required for " + eventType + ".");
                return;
            }

            RequireText(result, payload.command.idempotencyKey, "payload.command.idempotencyKey");
            RequireText(result, payload.command.commandType, "payload.command.commandType");
            RequireText(result, payload.command.status, "payload.command.status");
        }
    }

    private static void RequireText(
        P2EventValidationResult result,
        string value,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result.AddError(fieldName + " is required.");
        }
    }

    private static bool IsFiniteNonNegative(double value)
    {
        return !double.IsNaN(value) &&
               !double.IsInfinity(value) &&
               value >= 0d;
    }
}
