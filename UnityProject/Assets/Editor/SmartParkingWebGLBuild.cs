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
        EnsureP3BackendInScene();

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

        PostProcessWebGLTemplate(outputPath);
        Debug.Log($"SmartParking WebGL build completed: {outputPath}");
    }

    private static void ConfigureWebGLPlayer()
    {
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = true;
    }

    private static void EnsureP3BackendInScene()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        P3BackendSetupWizard.CreateOrUpdateSetup();

        GameObject bridge = GameObject.Find(BackendBridgeName);
        if (bridge != null)
        {
            UnityStateExporter legacyExporter = bridge.GetComponent<UnityStateExporter>();
            if (legacyExporter != null && legacyExporter.enabled)
            {
                Undo.RecordObject(legacyExporter, "Disable Legacy Snapshot Exporter");
                legacyExporter.enabled = false;
                EditorUtility.SetDirty(legacyExporter);
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void PostProcessWebGLTemplate(string outputPath)
    {
        string stylePath = Path.Combine(outputPath, "TemplateData", "style.css");
        if (File.Exists(stylePath))
        {
            string style = File.ReadAllText(stylePath);
            style = style.Replace(
                "body { padding: 0; margin: 0 }\n#unity-container { position: absolute }\n#unity-container.unity-desktop { left: 50%; top: 50%; transform: translate(-50%, -50%) }",
                "html, body { width: 100%; height: 100%; padding: 0; margin: 0; overflow: hidden; background: #1F1F20 }\n#unity-container { position: fixed; inset: 0; width: 100%; height: 100% }\n#unity-container.unity-desktop { left: 0; top: 0; transform: none }");
            style = style.Replace(
                "#unity-canvas { background: #1F1F20 }",
                "#unity-canvas { width: 100%; height: 100%; background: #1F1F20 }");
            style = style.Replace(
                "#unity-footer { position: relative }",
                "#unity-footer { position: absolute; left: 0; right: 0; bottom: 0; height: 38px; background: rgba(31,31,32,0.8) }");
            File.WriteAllText(stylePath, style);
        }

        string indexPath = Path.Combine(outputPath, "index.html");
        if (File.Exists(indexPath))
        {
            string html = File.ReadAllText(indexPath);
            html = html.Replace(
                "        // Desktop style: Render the game canvas in a window that can be maximized to fullscreen:\n\n        canvas.style.width = \"960px\";\n        canvas.style.height = \"600px\";",
                "        // Desktop style: fill the iframe supplied by the admin screen.\n        canvas.style.width = \"100vw\";\n        canvas.style.height = \"100vh\";");

            const string unityReadyToken = "}).then((unityInstance) => {";
            if (html.Contains(unityReadyToken) && !html.Contains("smartParkingApiKey"))
            {
                html = html.Replace(
                    unityReadyToken,
                    unityReadyToken +
                    "\n                window.smartParkingUnityInstance = unityInstance;" +
                    "\n                var smartParkingParameters = new URLSearchParams(window.location.search);" +
                    "\n                var smartParkingApiKey = smartParkingParameters.get(\"apiKey\");" +
                    "\n                var smartParkingBackendMode = smartParkingParameters.get(\"backendMode\");" +
                    "\n                if (smartParkingApiKey) {" +
                    "\n                  unityInstance.SendMessage(\"P3BackendSystem\", \"SetRuntimeApiKey\", smartParkingApiKey);" +
                    "\n                }" +
                    "\n                if (smartParkingBackendMode) {" +
                    "\n                  unityInstance.SendMessage(\"P3BackendSystem\", \"SetRuntimeBackendMode\", smartParkingBackendMode);" +
                    "\n                }");
            }
            File.WriteAllText(indexPath, html);
        }
    }
}
