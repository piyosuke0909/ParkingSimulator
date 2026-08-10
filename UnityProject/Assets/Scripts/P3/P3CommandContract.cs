using System;
using System.Collections.Generic;

public static class P3CommandContractConstants
{
    public const string BatchContractName = "smart-parking.command-batch";
    public const string CommandContractName = "smart-parking.command";
    public const string SchemaVersion = "1.0";
    public const string SetAreaPolicy = "SET_AREA_POLICY";

    public const string PolicyPriority = "PRIORITY";
    public const string PolicyClosed = "CLOSED";
    public const string PolicyRestricted = "RESTRICTED";
    public const string PolicyNormal = "NORMAL";
}

[Serializable]
public class P3CommandBatch
{
    public string contractName;
    public string schemaVersion;
    public string serverTimeUtc;
    public int leaseSeconds;
    public List<P3Command> commands = new List<P3Command>();
}

[Serializable]
public class P3Command
{
    public string contractName;
    public string schemaVersion;
    public string commandId;
    public string idempotencyKey;
    public string commandType;
    public string targetSourceId;
    public string targetSessionId;
    public string targetRunId;
    public string createdAtUtc;
    public string expiresAtUtc;
    public int priority;
    public string correlationId;
    public string issuedBy;
    public string reason;
    public P3CommandPayload payload;
}

[Serializable]
public class P3CommandPayload
{
    public string areaId;
    public string policy;
    public string effectiveUntilUtc;
    public string reason;
}
