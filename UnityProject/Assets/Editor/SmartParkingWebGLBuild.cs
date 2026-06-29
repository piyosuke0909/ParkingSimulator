using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SmartParkingWebGLBuild
{
    private const string ScenePath = "Assets/Scenes/Parking/SampleScene.unity";
    private const string OutputPath = "../../WebApp/frontend/public/unity-build";
    private const string BackendBridgeName = "BackendBridge";

    [MenuItem("SmartParking/Build WebGL")]
    public static void Build()
    {
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
        ConfigureWebGLPlayer();
        EnsureExporterInScene();

        string configuredOutputPath = Environment.GetEnvironmentVariable("SMARTPARKING_WEBGL_OUTPUT");
        string outputPath = string.IsNullOrWhiteSpace(configuredOutputPath)
            ? Path.GetFullPath(Path.Combine(Application.dataPath, OutputPath))
            : configuredOutputPath;
        Directory.CreateDirectory(outputPath);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = outputPath,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            throw new Exception($"WebGL build failed: {report.summary.result}");
        }

        Debug.Log($"SmartParking WebGL build completed: {outputPath}");
    }

    private static void ConfigureWebGLPlayer()
    {
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = true;
    }

    private static void EnsureExporterInScene()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject bridge = GameObject.Find(BackendBridgeName);

        if (bridge == null)
        {
            bridge = new GameObject(BackendBridgeName);
        }

        UnityStateExporter exporter = bridge.GetComponent<UnityStateExporter>();
        if (exporter == null)
        {
            exporter = bridge.AddComponent<UnityStateExporter>();
        }

        SerializedObject serialized = new SerializedObject(exporter);
        SetString(serialized, "backendSnapshotUrl", "http://localhost:8000/api/unity/snapshot");
        SetString(serialized, "sourceId", "unity-webgl-admin-01");
        SetFloat(serialized, "sendIntervalSeconds", 1f);
        SetBool(serialized, "autoFindParkingLotManager", true);
        SetBool(serialized, "includeSlots", true);
        SetBool(serialized, "includeCars", true);
        SetBool(serialized, "logSuccess", false);
        SetBool(serialized, "logFailure", true);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void SetString(SerializedObject serialized, string propertyName, string value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
        {
            property.stringValue = value;
        }
    }

    private static void SetFloat(SerializedObject serialized, string propertyName, float value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
        {
            property.floatValue = value;
        }
    }

    private static void SetBool(SerializedObject serialized, string propertyName, bool value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
        {
            property.boolValue = value;
        }
    }
}
