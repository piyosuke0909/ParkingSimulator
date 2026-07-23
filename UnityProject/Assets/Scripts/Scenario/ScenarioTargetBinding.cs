using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ScenarioAccessPointBinding
{
    [Tooltip("契約上のID。例: entrance-west / entrance-event")]
    public string canonicalAccessPointId;

    [Tooltip("VehicleSpawnManagerのEntranceExitPair.pairName。例: west_gate")]
    public string scenePairName;

    [Tooltip("ログに出す代表IDとして使用します。")]
    public bool primaryForLogging;
}

[Serializable]
public class ScenarioAreaBinding
{
    [Tooltip("契約上のID。例: area-a / area-event")]
    public string canonicalAreaId;

    [Tooltip("ParkingSlot.areaId。例: A")]
    public string sceneAreaId;

    [Tooltip("ログに出す代表IDとして使用します。")]
    public bool primaryForLogging = true;
}

public class ScenarioTargetBinding : MonoBehaviour
{
    public List<ScenarioAccessPointBinding> accessPointBindings = new List<ScenarioAccessPointBinding>();
    public List<ScenarioAreaBinding> areaBindings = new List<ScenarioAreaBinding>();

    public bool MatchesAccessPointTarget(string canonicalTargetId, string scenePairName)
    {
        if (string.IsNullOrEmpty(canonicalTargetId) || string.IsNullOrEmpty(scenePairName))
        {
            return false;
        }

        foreach (ScenarioAccessPointBinding binding in accessPointBindings)
        {
            if (binding == null)
            {
                continue;
            }

            if (!string.Equals(binding.canonicalAccessPointId, canonicalTargetId, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(binding.scenePairName, scenePairName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return string.Equals(canonicalTargetId, scenePairName, StringComparison.Ordinal);
    }

    public bool MatchesAreaTarget(string canonicalTargetId, string sceneAreaId)
    {
        if (string.IsNullOrEmpty(canonicalTargetId) || string.IsNullOrEmpty(sceneAreaId))
        {
            return false;
        }

        foreach (ScenarioAreaBinding binding in areaBindings)
        {
            if (binding == null)
            {
                continue;
            }

            if (!string.Equals(binding.canonicalAreaId, canonicalTargetId, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(binding.sceneAreaId, sceneAreaId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return string.Equals(canonicalTargetId, sceneAreaId, StringComparison.Ordinal);
    }

    public string GetPrimaryCanonicalAccessPointId(string scenePairName)
    {
        ScenarioAccessPointBinding firstMatch = null;

        foreach (ScenarioAccessPointBinding binding in accessPointBindings)
        {
            if (binding == null ||
                !string.Equals(binding.scenePairName, scenePairName, StringComparison.Ordinal))
            {
                continue;
            }

            if (firstMatch == null)
            {
                firstMatch = binding;
            }

            if (binding.primaryForLogging && !string.IsNullOrEmpty(binding.canonicalAccessPointId))
            {
                return binding.canonicalAccessPointId;
            }
        }

        if (firstMatch != null && !string.IsNullOrEmpty(firstMatch.canonicalAccessPointId))
        {
            return firstMatch.canonicalAccessPointId;
        }

        return NormalizeFallbackId(scenePairName, "entrance");
    }

    public string GetCanonicalAreaId(string sceneAreaId)
    {
        ScenarioAreaBinding firstMatch = null;

        foreach (ScenarioAreaBinding binding in areaBindings)
        {
            if (binding == null ||
                !string.Equals(binding.sceneAreaId, sceneAreaId, StringComparison.Ordinal))
            {
                continue;
            }

            if (firstMatch == null)
            {
                firstMatch = binding;
            }

            if (binding.primaryForLogging && !string.IsNullOrEmpty(binding.canonicalAreaId))
            {
                return binding.canonicalAreaId;
            }
        }

        if (firstMatch != null && !string.IsNullOrEmpty(firstMatch.canonicalAreaId))
        {
            return firstMatch.canonicalAreaId;
        }

        return NormalizeFallbackId(sceneAreaId, "area");
    }

    public bool HasResolvedAccessPoint(string canonicalAccessPointId)
    {
        foreach (ScenarioAccessPointBinding binding in accessPointBindings)
        {
            if (binding == null)
            {
                continue;
            }

            if (string.Equals(binding.canonicalAccessPointId, canonicalAccessPointId, StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(binding.scenePairName))
            {
                return true;
            }
        }

        return false;
    }

    public bool HasResolvedArea(string canonicalAreaId)
    {
        foreach (ScenarioAreaBinding binding in areaBindings)
        {
            if (binding == null)
            {
                continue;
            }

            if (string.Equals(binding.canonicalAreaId, canonicalAreaId, StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(binding.sceneAreaId))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeFallbackId(string source, string prefix)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return prefix + "-unknown";
        }

        string normalized = source.Trim().ToLowerInvariant()
            .Replace('_', '-')
            .Replace(' ', '-');

        if (normalized.StartsWith(prefix + "-", StringComparison.Ordinal))
        {
            return normalized;
        }

        return prefix + "-" + normalized;
    }
}
