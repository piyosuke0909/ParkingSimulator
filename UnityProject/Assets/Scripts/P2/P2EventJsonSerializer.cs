using System;
using System.Text;
using UnityEngine;

/// <summary>
/// P2 EventをJSON Lines向けの1行JSONへ変換します。
/// UnityのJsonUtilityはnullの入れ子クラスを既定値オブジェクトとして
/// 出力する場合があるため、共通ヘッダーと存在するPayloadだけを個別に
/// シリアライズして結合します。
/// </summary>
public static class P2EventJsonSerializer
{
    [Serializable]
    private class EventEnvelope
    {
        public string contractName;
        public string schemaVersion;
        public string eventId;
        public string eventType;
        public string sourceSystem;
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
        public string entityType;
        public string entityId;
    }


    [Serializable]
    private class OptionalCommandIdEnvelope
    {
        public string commandId;
    }

    public static string ToJson(P2SimulationEvent simulationEvent)
    {
        if (simulationEvent == null)
        {
            throw new ArgumentNullException(nameof(simulationEvent));
        }

        EventEnvelope envelope = new EventEnvelope
        {
            contractName = simulationEvent.contractName,
            schemaVersion = simulationEvent.schemaVersion,
            eventId = simulationEvent.eventId,
            eventType = simulationEvent.eventType,
            sourceSystem = simulationEvent.sourceSystem,
            generatedAtUtc = simulationEvent.generatedAtUtc,
            sceneName = simulationEvent.sceneName,
            sessionId = simulationEvent.sessionId,
            runId = simulationEvent.runId,
            sequenceNumber = simulationEvent.sequenceNumber,
            scenarioId = simulationEvent.scenarioId,
            scenarioVersion = simulationEvent.scenarioVersion,
            facilityId = simulationEvent.facilityId,
            simulationTimeSeconds = simulationEvent.simulationTimeSeconds,
            correlationId = simulationEvent.correlationId,
            entityType = simulationEvent.entityType,
            entityId = simulationEvent.entityId
        };

        string envelopeJson = JsonUtility.ToJson(envelope, false);

        if (string.IsNullOrEmpty(envelopeJson) ||
            envelopeJson[envelopeJson.Length - 1] != '}')
        {
            throw new InvalidOperationException("Event envelope serialization failed.");
        }

        StringBuilder builder = new StringBuilder(envelopeJson.Length + 512);
        builder.Append(envelopeJson, 0, envelopeJson.Length - 1);

        if (!string.IsNullOrWhiteSpace(simulationEvent.commandId))
        {
            string commandIdJson = JsonUtility.ToJson(
                new OptionalCommandIdEnvelope { commandId = simulationEvent.commandId },
                false
            );

            builder.Append(',');
            builder.Append(commandIdJson, 1, commandIdJson.Length - 2);
        }

        builder.Append(",\"payload\":{");

        bool hasPayload = false;
        P2EventPayload payload = simulationEvent.payload;

        if (payload != null)
        {
            AppendPayload(builder, "scenario", payload.scenario, ref hasPayload);
            AppendPayload(builder, "scenarioFactor", payload.scenarioFactor, ref hasPayload);
            AppendPayload(builder, "selection", payload.selection, ref hasPayload);
            AppendPayload(builder, "vehicle", payload.vehicle, ref hasPayload);
        }

        builder.Append("}}");
        return builder.ToString();
    }

    private static void AppendPayload(
        StringBuilder builder,
        string propertyName,
        object payload,
        ref bool hasPayload)
    {
        if (payload == null)
        {
            return;
        }

        string payloadJson = JsonUtility.ToJson(payload, false);

        if (string.IsNullOrEmpty(payloadJson))
        {
            throw new InvalidOperationException(
                "Payload serialization failed: " + propertyName
            );
        }

        if (hasPayload)
        {
            builder.Append(',');
        }

        builder.Append('\"');
        builder.Append(propertyName);
        builder.Append("\":");
        builder.Append(payloadJson);
        hasPayload = true;
    }
}
