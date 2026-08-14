using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Resolves P3 authentication secrets without serializing them into Unity scenes/prefabs.
/// Priority: runtime override -> OS environment variable -> project-root .env.
/// WebGL builds intentionally do not read local files/environment variables; inject a
/// non-secret/local-demo value at runtime with SetRuntimeApiKey/SetRuntimeBearerToken.
/// </summary>
public static class P3SecretProvider
{
    private static readonly Dictionary<string, string> DotEnvValues =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static bool dotEnvLoaded;
    private static string runtimeApiKey = string.Empty;
    private static string runtimeBearerToken = string.Empty;

    public static void SetRuntimeApiKey(string value)
    {
        runtimeApiKey = value ?? string.Empty;
    }

    public static void SetRuntimeBearerToken(string value)
    {
        runtimeBearerToken = value ?? string.Empty;
    }

    public static void ClearRuntimeOverrides()
    {
        runtimeApiKey = string.Empty;
        runtimeBearerToken = string.Empty;
    }

    public static void ReloadDotEnv()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        dotEnvLoaded = false;
        DotEnvValues.Clear();
#endif
    }

    public static bool TryResolveApiKey(string variableName, out string value)
    {
        if (!string.IsNullOrWhiteSpace(runtimeApiKey))
        {
            value = runtimeApiKey;
            return true;
        }

        return TryResolveVariable(variableName, out value);
    }

    public static bool TryResolveBearerToken(string variableName, out string value)
    {
        if (!string.IsNullOrWhiteSpace(runtimeBearerToken))
        {
            value = runtimeBearerToken;
            return true;
        }

        return TryResolveVariable(variableName, out value);
    }

    private static bool TryResolveVariable(string variableName, out string value)
    {
        value = string.Empty;

        if (string.IsNullOrWhiteSpace(variableName))
        {
            return false;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // Browser builds cannot keep a .env/API key secret. Use runtime injection only.
        return false;
#else
        string key = variableName.Trim();

        string environmentValue = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            value = environmentValue.Trim();
            return true;
        }

        EnsureDotEnvLoaded();

        string dotEnvValue;
        if (DotEnvValues.TryGetValue(key, out dotEnvValue) &&
            !string.IsNullOrWhiteSpace(dotEnvValue))
        {
            value = dotEnvValue.Trim();
            return true;
        }

        return false;
#endif
    }

#if !UNITY_WEBGL || UNITY_EDITOR
    private static void EnsureDotEnvLoaded()
    {
        if (dotEnvLoaded)
        {
            return;
        }

        dotEnvLoaded = true;
        DotEnvValues.Clear();

        string path = FindDotEnvPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            foreach (string rawLine in File.ReadAllLines(path))
            {
                ParseDotEnvLine(rawLine);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[P3] Failed to read project-root .env. Authentication secret was not loaded. " +
                exception.GetType().Name
            );
        }
    }

    private static string FindDotEnvPath()
    {
        string projectOrApplicationRoot = string.Empty;

        try
        {
            if (!string.IsNullOrWhiteSpace(Application.dataPath))
            {
                DirectoryInfo parent = Directory.GetParent(Application.dataPath);
                if (parent != null)
                {
                    projectOrApplicationRoot = parent.FullName;
                }
            }
        }
        catch
        {
            projectOrApplicationRoot = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(projectOrApplicationRoot))
        {
            string candidate = Path.Combine(projectOrApplicationRoot, ".env");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        try
        {
            string currentDirectoryCandidate = Path.Combine(
                Directory.GetCurrentDirectory(),
                ".env"
            );
            if (File.Exists(currentDirectoryCandidate))
            {
                return currentDirectoryCandidate;
            }
        }
        catch
        {
            // Ignore current-directory lookup failures.
        }

        return string.Empty;
    }

    private static void ParseDotEnvLine(string rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine))
        {
            return;
        }

        string line = rawLine.Trim().TrimStart('\uFEFF');
        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
        {
            return;
        }

        if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
        {
            line = line.Substring(7).TrimStart();
        }

        int separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            return;
        }

        string key = line.Substring(0, separatorIndex).Trim();
        string value = line.Substring(separatorIndex + 1).Trim();

        if (key.Length == 0)
        {
            return;
        }

        if (value.Length >= 2)
        {
            bool doubleQuoted = value[0] == '"' && value[value.Length - 1] == '"';
            bool singleQuoted = value[0] == '\'' && value[value.Length - 1] == '\'';
            if (doubleQuoted || singleQuoted)
            {
                value = value.Substring(1, value.Length - 2);
            }
        }

        DotEnvValues[key] = value;
    }
#endif
}
