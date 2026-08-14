using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class P3PendingFileStore
{
    private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);

    public static bool WriteIfMissing(string directory, string fileName, string content)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, SanitizePathPart(fileName, "pending.json"));

        if (File.Exists(path))
        {
            return false;
        }

        WriteTextAtomically(path, content ?? string.Empty);
        return true;
    }

    public static string[] GetSortedJsonFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return new string[0];
        }

        string[] files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.Ordinal);
        return files;
    }

    public static int CountJsonFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        return Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly).Length;
    }

    public static int RequeueJsonFiles(string failedDirectory, string pendingDirectory)
    {
        if (!Directory.Exists(failedDirectory))
        {
            return 0;
        }

        Directory.CreateDirectory(pendingDirectory);
        string[] files = GetSortedJsonFiles(failedDirectory);
        int movedCount = 0;

        foreach (string sourcePath in files)
        {
            string destinationPath = Path.Combine(
                pendingDirectory,
                Path.GetFileName(sourcePath)
            );

            if (!File.Exists(destinationPath))
            {
                File.Move(sourcePath, destinationPath);
                movedCount++;
            }
            else
            {
                File.Delete(sourcePath);
            }

            string errorPath = sourcePath + ".error.txt";
            if (File.Exists(errorPath))
            {
                File.Delete(errorPath);
            }
        }

        return movedCount;
    }

    public static void DeleteFiles(IEnumerable<string> paths)
    {
        if (paths == null)
        {
            return;
        }

        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                continue;
            }

            File.Delete(path);
        }
    }

    public static string MoveToFailed(string sourcePath, string failedDirectory, string reason)
    {
        Directory.CreateDirectory(failedDirectory);

        string originalName = Path.GetFileName(sourcePath);
        string destination = Path.Combine(failedDirectory, originalName);

        if (File.Exists(destination))
        {
            destination = Path.Combine(
                failedDirectory,
                Path.GetFileNameWithoutExtension(originalName) + "-" +
                DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ") +
                Path.GetExtension(originalName)
            );
        }

        if (File.Exists(sourcePath))
        {
            File.Move(sourcePath, destination);
        }

        string errorPath = destination + ".error.txt";
        File.WriteAllText(
            errorPath,
            reason ?? string.Empty,
            Utf8WithoutBom
        );

        return destination;
    }

    public static string WriteFailedPayload(
        string failedDirectory,
        string fileName,
        string content,
        string reason)
    {
        Directory.CreateDirectory(failedDirectory);
        string sanitized = SanitizePathPart(fileName, "failed.json");
        string path = Path.Combine(failedDirectory, sanitized);

        if (File.Exists(path))
        {
            path = Path.Combine(
                failedDirectory,
                Path.GetFileNameWithoutExtension(sanitized) + "-" +
                DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ") +
                Path.GetExtension(sanitized)
            );
        }

        WriteTextAtomically(path, content ?? string.Empty);
        File.WriteAllText(path + ".error.txt", reason ?? string.Empty, Utf8WithoutBom);
        return path;
    }

    public static void WriteTextAtomically(string path, string text)
    {
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, text ?? string.Empty, Utf8WithoutBom);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(temporaryPath, path);
    }

    public static string SanitizePathPart(string value, string fallback)
    {
        string result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(invalid, '-');
        }

        result = result.Replace(' ', '-');
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }
}
