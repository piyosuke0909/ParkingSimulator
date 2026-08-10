using System;
using System.Collections.Generic;
using UnityEngine;

public enum P3AreaPolicy
{
    NORMAL,
    PRIORITY,
    CLOSED,
    RESTRICTED
}

public class P3AreaPolicyRuntime : MonoBehaviour
{
    public static P3AreaPolicyRuntime Instance { get; private set; }

    [Header("References")]
    public ParkingLotManager parkingLotManager;

    [Header("Selection Weight Multipliers")]
    [Min(1f)]
    public float priorityWeightMultiplier = 3f;

    [Range(0f, 1f)]
    public float restrictedWeightMultiplier = 0.25f;

    [Header("Logging")]
    public bool logPolicyChanges = true;
    public bool logAutomaticExpiry = true;

    [Header("Current Policies (Read Only)")]
    [SerializeField] private string areaAPolicy = "NORMAL";
    [SerializeField] private string areaBPolicy = "NORMAL";
    [SerializeField] private string areaCPolicy = "NORMAL";
    [SerializeField] private string areaDPolicy = "NORMAL";

    private sealed class PolicyEntry
    {
        public P3AreaPolicy policy;
        public string commandId;
        public string reason;
        public bool hasEffectiveUntilUtc;
        public DateTimeOffset effectiveUntilUtc;
    }

    private readonly Dictionary<string, PolicyEntry> policies =
        new Dictionary<string, PolicyEntry>(StringComparer.OrdinalIgnoreCase);

    private string observedRunId = string.Empty;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning(name + ": P3AreaPolicyRuntime is duplicated. The first Instance is used.");
        }
        else
        {
            Instance = this;
        }

        ResolveReferences();
    }

    private void Update()
    {
        ResetPoliciesWhenRunChanges();
        ExpirePolicies(DateTimeOffset.UtcNow);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void ResolveReferences()
    {
        if (parkingLotManager != null)
        {
            return;
        }

#if UNITY_2023_1_OR_NEWER
        parkingLotManager = FindFirstObjectByType<ParkingLotManager>();
#else
        parkingLotManager = FindObjectOfType<ParkingLotManager>();
#endif
    }

    public bool TryApply(
        P3Command command,
        out string previousPolicy,
        out string appliedPolicy,
        out string reasonCode,
        out string message)
    {
        previousPolicy = P3CommandContractConstants.PolicyNormal;
        appliedPolicy = P3CommandContractConstants.PolicyNormal;
        reasonCode = "EXECUTION_ERROR";
        message = "Area policy could not be applied.";

        if (command == null || command.payload == null)
        {
            reasonCode = "INVALID_PAYLOAD";
            message = "Command payload is missing.";
            return false;
        }

        ResolveReferences();
        string areaId = P3CommandContractValidator.NormalizeAreaId(command.payload.areaId);
        if (!AreaExists(areaId))
        {
            reasonCode = "AREA_NOT_FOUND";
            message = "The specified area does not exist.";
            return false;
        }

        P3AreaPolicy requestedPolicy;
        if (!TryParsePolicy(command.payload.policy, out requestedPolicy))
        {
            reasonCode = "INVALID_PAYLOAD";
            message = "The specified area policy is invalid.";
            return false;
        }

        previousPolicy = GetPolicy(areaId).ToString();

        if (requestedPolicy == P3AreaPolicy.NORMAL)
        {
            policies.Remove(areaId);
            appliedPolicy = P3AreaPolicy.NORMAL.ToString();
        }
        else
        {
            PolicyEntry entry = new PolicyEntry
            {
                policy = requestedPolicy,
                commandId = command.commandId ?? string.Empty,
                reason = command.payload.reason ?? string.Empty
            };

            if (!string.IsNullOrWhiteSpace(command.payload.effectiveUntilUtc))
            {
                DateTimeOffset parsed;
                if (!P3CommandContractValidator.TryParseUtc(command.payload.effectiveUntilUtc, out parsed))
                {
                    reasonCode = "INVALID_PAYLOAD";
                    message = "effectiveUntilUtc is invalid.";
                    return false;
                }

                entry.hasEffectiveUntilUtc = true;
                entry.effectiveUntilUtc = parsed;
            }

            policies[areaId] = entry;
            appliedPolicy = requestedPolicy.ToString();
        }

        reasonCode = "POLICY_APPLIED";
        message = areaId + " area policy changed from " + previousPolicy + " to " + appliedPolicy + ".";

        RefreshPolicyDisplay();

        if (logPolicyChanges)
        {
            Debug.Log("[P3Command] " + message + " commandId=" + (command.commandId ?? string.Empty));
        }

        return true;
    }

    public P3AreaPolicy GetPolicy(string sceneOrCanonicalAreaId)
    {
        string areaId = P3CommandContractValidator.NormalizeAreaId(sceneOrCanonicalAreaId);
        PolicyEntry entry;
        if (!policies.TryGetValue(areaId, out entry) || entry == null)
        {
            return P3AreaPolicy.NORMAL;
        }

        if (entry.hasEffectiveUntilUtc && DateTimeOffset.UtcNow >= entry.effectiveUntilUtc)
        {
            policies.Remove(areaId);
            return P3AreaPolicy.NORMAL;
        }

        return entry.policy;
    }

    public bool IsClosed(string sceneOrCanonicalAreaId)
    {
        return GetPolicy(sceneOrCanonicalAreaId) == P3AreaPolicy.CLOSED;
    }

    public float GetSelectionWeightMultiplier(string sceneOrCanonicalAreaId)
    {
        switch (GetPolicy(sceneOrCanonicalAreaId))
        {
            case P3AreaPolicy.PRIORITY:
                return Mathf.Max(1f, priorityWeightMultiplier);
            case P3AreaPolicy.RESTRICTED:
                return Mathf.Clamp01(restrictedWeightMultiplier);
            case P3AreaPolicy.CLOSED:
                return 0f;
            default:
                return 1f;
        }
    }

    private bool AreaExists(string areaId)
    {
        if (string.IsNullOrWhiteSpace(areaId))
        {
            return false;
        }

        if (parkingLotManager == null || parkingLotManager.allSlots == null)
        {
            return areaId == "A" || areaId == "B" || areaId == "C" || areaId == "D";
        }

        foreach (ParkingSlot slot in parkingLotManager.allSlots)
        {
            if (slot != null &&
                string.Equals(
                    P3CommandContractValidator.NormalizeAreaId(slot.areaId),
                    areaId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void ResetPoliciesWhenRunChanges()
    {
        string currentRunId = P2SimulationRunContext.CurrentRunId;
        if (string.IsNullOrWhiteSpace(currentRunId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(observedRunId))
        {
            observedRunId = currentRunId;
            return;
        }

        if (string.Equals(observedRunId, currentRunId, StringComparison.Ordinal))
        {
            return;
        }

        policies.Clear();
        observedRunId = currentRunId;
        RefreshPolicyDisplay();

        if (logPolicyChanges)
        {
            Debug.Log("[P3Command] Area policies were reset because the simulation run changed.");
        }
    }

    private void ExpirePolicies(DateTimeOffset nowUtc)
    {
        if (policies.Count == 0)
        {
            return;
        }

        List<string> expired = null;
        foreach (KeyValuePair<string, PolicyEntry> pair in policies)
        {
            PolicyEntry entry = pair.Value;
            if (entry != null && entry.hasEffectiveUntilUtc && nowUtc >= entry.effectiveUntilUtc)
            {
                if (expired == null)
                {
                    expired = new List<string>();
                }
                expired.Add(pair.Key);
            }
        }

        if (expired == null)
        {
            return;
        }

        foreach (string areaId in expired)
        {
            policies.Remove(areaId);
            RefreshPolicyDisplay();
            if (logAutomaticExpiry)
            {
                Debug.Log("[P3Command] Area policy automatically returned to NORMAL. areaId=" + areaId);
            }
        }
    }

    private void RefreshPolicyDisplay()
    {
        areaAPolicy = GetPolicy("A").ToString();
        areaBPolicy = GetPolicy("B").ToString();
        areaCPolicy = GetPolicy("C").ToString();
        areaDPolicy = GetPolicy("D").ToString();
    }

    private static bool TryParsePolicy(string value, out P3AreaPolicy policy)
    {
        string normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return Enum.TryParse(normalized, false, out policy);
    }
}
