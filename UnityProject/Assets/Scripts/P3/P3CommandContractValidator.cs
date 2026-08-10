using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

[Serializable]
public class P3CommandValidationResult
{
    public bool isValid = true;
    public List<string> errors = new List<string>();

    public void AddError(string message)
    {
        isValid = false;
        errors.Add(message ?? string.Empty);
    }

    public string BuildMessage()
    {
        return errors == null || errors.Count == 0
            ? string.Empty
            : string.Join(" | ", errors);
    }
}

public class P3CommandContractValidator : MonoBehaviour
{
    public P3CommandValidationResult ValidateBatch(P3CommandBatch batch, int maximumCount)
    {
        P3CommandValidationResult result = new P3CommandValidationResult();

        if (batch == null)
        {
            result.AddError("Command batch is null.");
            return result;
        }

        RequireExact(result, batch.contractName, P3CommandContractConstants.BatchContractName, "contractName");
        RequireExact(result, batch.schemaVersion, P3CommandContractConstants.SchemaVersion, "schemaVersion");
        RequireDateTime(result, batch.serverTimeUtc, "serverTimeUtc");

        if (batch.leaseSeconds <= 0)
        {
            result.AddError("leaseSeconds must be greater than zero.");
        }

        if (batch.commands == null)
        {
            result.AddError("commands is required.");
            return result;
        }

        int safeMaximum = Mathf.Clamp(maximumCount, 1, P3BackendSettings.BackendMaximumCommandCount);
        if (batch.commands.Count > safeMaximum)
        {
            result.AddError("commands exceeds the configured maximum count of " + safeMaximum + ".");
        }

        return result;
    }

    public P3CommandValidationResult ValidateCommand(P3Command command)
    {
        P3CommandValidationResult result = new P3CommandValidationResult();

        if (command == null)
        {
            result.AddError("Command is null.");
            return result;
        }

        RequireExact(result, command.contractName, P3CommandContractConstants.CommandContractName, "contractName");
        RequireExact(result, command.schemaVersion, P3CommandContractConstants.SchemaVersion, "schemaVersion");
        RequireText(result, command.commandId, "commandId");
        RequireText(result, command.idempotencyKey, "idempotencyKey");
        RequireText(result, command.commandType, "commandType");
        RequireText(result, command.targetSourceId, "targetSourceId");
        RequireText(result, command.targetSessionId, "targetSessionId");
        RequireText(result, command.targetRunId, "targetRunId");
        RequireDateTime(result, command.createdAtUtc, "createdAtUtc");
        RequireDateTime(result, command.expiresAtUtc, "expiresAtUtc");

        if (!string.Equals(command.commandType, P3CommandContractConstants.SetAreaPolicy, StringComparison.Ordinal))
        {
            result.AddError("Unknown commandType: " + (command.commandType ?? string.Empty));
            return result;
        }

        if (command.payload == null)
        {
            result.AddError("payload is required for SET_AREA_POLICY.");
            return result;
        }

        string areaId = NormalizeAreaId(command.payload.areaId);
        if (areaId != "A" && areaId != "B" && areaId != "C" && areaId != "D")
        {
            result.AddError("payload.areaId must be A, B, C, or D.");
        }

        string policy = (command.payload.policy ?? string.Empty).Trim().ToUpperInvariant();
        if (policy != P3CommandContractConstants.PolicyPriority &&
            policy != P3CommandContractConstants.PolicyClosed &&
            policy != P3CommandContractConstants.PolicyRestricted &&
            policy != P3CommandContractConstants.PolicyNormal)
        {
            result.AddError("payload.policy must be PRIORITY, CLOSED, RESTRICTED, or NORMAL.");
        }

        if (!string.IsNullOrWhiteSpace(command.payload.effectiveUntilUtc))
        {
            RequireDateTime(result, command.payload.effectiveUntilUtc, "payload.effectiveUntilUtc");
        }

        if (!string.IsNullOrEmpty(command.payload.reason) && command.payload.reason.Length > 200)
        {
            result.AddError("payload.reason must be 200 characters or fewer.");
        }

        return result;
    }

    public static bool TryParseUtc(string value, out DateTimeOffset parsed)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out parsed
        );
    }

    public static string NormalizeAreaId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim().ToUpperInvariant();
        if (normalized.StartsWith("AREA-", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(5);
        }

        return normalized;
    }

    private static void RequireText(P3CommandValidationResult result, string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result.AddError(fieldName + " is required.");
        }
    }

    private static void RequireExact(
        P3CommandValidationResult result,
        string value,
        string expected,
        string fieldName)
    {
        if (!string.Equals(value, expected, StringComparison.Ordinal))
        {
            result.AddError(fieldName + " must be '" + expected + "'.");
        }
    }

    private static void RequireDateTime(P3CommandValidationResult result, string value, string fieldName)
    {
        RequireText(result, value, fieldName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        DateTimeOffset parsed;
        if (!TryParseUtc(value, out parsed))
        {
            result.AddError(fieldName + " must be a valid date-time value.");
        }
    }
}
