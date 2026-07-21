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
    public string canonicalAreaId;
    public string sceneAreaId;
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

        // P0確定前でもpairNameを直接targetIdとして使用可能にするフォールバック。
        return string.Equals(canonicalTargetId, scenePairName, StringComparison.Ordinal);
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
        foreach (ScenarioAreaBinding binding in areaBindings)
        {
            if (binding == null)
            {
                continue;
            }

            if (string.Equals(binding.sceneAreaId, sceneAreaId, StringComparison.Ordinal))
            {
                return binding.canonicalAreaId;
            }
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
