#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class P3BackendSetupWizard
{
    private const string RootObjectName = "P3BackendSystem";

    [MenuItem("Tools/Smart Parking/P3/Create or Update Backend Transmission Setup")]
    public static void CreateOrUpdateSetup()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[P3] No loaded active Scene was found.");
            return;
        }

        GameObject root = FindRootInScene(scene, RootObjectName);
        if (root == null)
        {
            root = new GameObject(RootObjectName);
            Undo.RegisterCreatedObjectUndo(root, "Create P3 Backend System");
            SceneManager.MoveGameObjectToScene(root, scene);
        }

        P3BackendSettings settings = GetOrAddComponent<P3BackendSettings>(root);
        P3TransmissionStatus status = GetOrAddComponent<P3TransmissionStatus>(root);
        P3EventHttpSender eventSender = GetOrAddComponent<P3EventHttpSender>(root);
        P3SnapshotHttpSender snapshotSender = GetOrAddComponent<P3SnapshotHttpSender>(root);
        P3BackendHealthChecker healthChecker = GetOrAddComponent<P3BackendHealthChecker>(root);
        P3CommandStatus commandStatus = GetOrAddComponent<P3CommandStatus>(root);
        P3CommandContractValidator commandValidator = GetOrAddComponent<P3CommandContractValidator>(root);
        P3AreaPolicyRuntime areaPolicyRuntime = GetOrAddComponent<P3AreaPolicyRuntime>(root);
        P3CommandPollingClient commandPollingClient = GetOrAddComponent<P3CommandPollingClient>(root);

        P2SimulationEventPublisher eventPublisher = FindSceneObject<P2SimulationEventPublisher>();
        P2EventContractValidator eventValidator = FindSceneObject<P2EventContractValidator>();
        P2LocalSnapshotWriter snapshotWriter = FindSceneObject<P2LocalSnapshotWriter>();
        P2SnapshotContractValidator snapshotValidator = FindSceneObject<P2SnapshotContractValidator>();
        P2SimulationSnapshotBuilder snapshotBuilder = FindSceneObject<P2SimulationSnapshotBuilder>();
        VehicleSpawnManager vehicleSpawnManager = FindSceneObject<VehicleSpawnManager>();
        ParkingLotManager parkingLotManager = FindSceneObject<ParkingLotManager>();

        Undo.RecordObject(settings, "Configure P3 Backend Settings");
        settings.baseUrl = "http://localhost:8000";
        settings.healthEndpoint = "/api/health";
        settings.snapshotEndpoint = "/api/v1/snapshots";
        settings.eventEndpoint = "/api/v1/events";
        settings.commandEndpoint = "/api/v1/commands";
        settings.sourceId = "unity-webgl-admin-01";
        settings.enableSnapshotTransmission = true;
        settings.enableEventTransmission = true;
        settings.enableHealthCheck = true;
        settings.enableCommandPolling = true;
        settings.eventBatchFormat = P3EventBatchFormat.Ndjson;
        settings.eventBatchSize = P3BackendSettings.BackendMaximumEventBatchCount;
        settings.eventFlushIntervalSeconds = Mathf.Max(0.1f, settings.eventFlushIntervalSeconds);
        settings.eventMaximumRequestBytes = P3BackendSettings.BackendMaximumEventRequestBytes;
        settings.snapshotMaximumRequestBytes = P3BackendSettings.BackendMaximumSnapshotRequestBytes;
        settings.requestTimeoutSeconds = 10f;
        settings.commandPollingIntervalSeconds = 1f;
        settings.commandRequestTimeoutSeconds = 5f;
        settings.commandMaximumCount = P3BackendSettings.BackendMaximumCommandCount;
        settings.commandRetryInitialDelaySeconds = 2f;
        settings.commandRetryMaximumDelaySeconds = 30f;
        settings.treatHttp409AsSuccess = false;
        settings.authenticationMode = P3AuthenticationMode.ApiKeyHeader;
        settings.apiKeyHeaderName = "X-API-Key";
        settings.retryDelaySeconds = Mathf.Max(0.1f, settings.retryDelaySeconds);
        settings.retryCooldownSeconds = Mathf.Max(1f, settings.retryCooldownSeconds);
        settings.maxRetryAttemptsBeforeCooldown = Mathf.Max(
            1,
            settings.maxRetryAttemptsBeforeCooldown
        );
        settings.healthCheckIntervalSeconds = Mathf.Max(1f, settings.healthCheckIntervalSeconds);

        Undo.RecordObject(eventSender, "Configure P3 Event Sender");
        eventSender.settings = settings;
        eventSender.transmissionStatus = status;
        eventSender.eventPublisher = eventPublisher;
        eventSender.contractValidator = eventValidator;

        Undo.RecordObject(snapshotSender, "Configure P3 Snapshot Sender");
        snapshotSender.settings = settings;
        snapshotSender.transmissionStatus = status;
        snapshotSender.snapshotWriter = snapshotWriter;
        snapshotSender.contractValidator = snapshotValidator;

        Undo.RecordObject(healthChecker, "Configure P3 Health Checker");
        healthChecker.settings = settings;
        healthChecker.transmissionStatus = status;

        Undo.RecordObject(areaPolicyRuntime, "Configure P3 Area Policy Runtime");
        areaPolicyRuntime.parkingLotManager = parkingLotManager;

        Undo.RecordObject(commandPollingClient, "Configure P3 Command Polling");
        commandPollingClient.settings = settings;
        commandPollingClient.commandStatus = commandStatus;
        commandPollingClient.contractValidator = commandValidator;
        commandPollingClient.areaPolicyRuntime = areaPolicyRuntime;
        commandPollingClient.eventPublisher = eventPublisher;

        if (vehicleSpawnManager != null)
        {
            Undo.RecordObject(vehicleSpawnManager, "Connect P3 Area Policy");
            vehicleSpawnManager.p3AreaPolicyRuntime = areaPolicyRuntime;
            EditorUtility.SetDirty(vehicleSpawnManager);
        }

        if (snapshotBuilder != null)
        {
            Undo.RecordObject(snapshotBuilder, "Connect P3 Area Policy To Snapshot");
            snapshotBuilder.p3AreaPolicyRuntime = areaPolicyRuntime;
            EditorUtility.SetDirty(snapshotBuilder);
        }

        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(status);
        EditorUtility.SetDirty(eventSender);
        EditorUtility.SetDirty(snapshotSender);
        EditorUtility.SetDirty(healthChecker);
        EditorUtility.SetDirty(commandStatus);
        EditorUtility.SetDirty(commandValidator);
        EditorUtility.SetDirty(areaPolicyRuntime);
        EditorUtility.SetDirty(commandPollingClient);
        EditorUtility.SetDirty(root);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;

        if (eventPublisher == null || snapshotWriter == null)
        {
            Debug.LogWarning(
                "[P3] Setup was created, but one or more P2 sources were not found. " +
                "Run the P2 Event and Snapshot setup wizards, then run this P3 setup again."
            );
        }
        else
        {
            Debug.Log(
                "[P3] Backend transmission setup was created or updated. " +
                "Confirmed Snapshot/Event and Command v1.0 contract values were applied to P3BackendSystem. " +
                "Set the local API key in P3BackendSettings before connecting to Backend."
            );
        }
    }

    [MenuItem("Tools/Smart Parking/P3/Validate Backend Transmission Setup")]
    public static void ValidateSetup()
    {
        P3BackendSettings settings = FindSceneObject<P3BackendSettings>();
        P3TransmissionStatus status = FindSceneObject<P3TransmissionStatus>();
        P3EventHttpSender eventSender = FindSceneObject<P3EventHttpSender>();
        P3SnapshotHttpSender snapshotSender = FindSceneObject<P3SnapshotHttpSender>();
        P3BackendHealthChecker healthChecker = FindSceneObject<P3BackendHealthChecker>();
        P3CommandStatus commandStatus = FindSceneObject<P3CommandStatus>();
        P3CommandContractValidator commandValidator = FindSceneObject<P3CommandContractValidator>();
        P3AreaPolicyRuntime areaPolicyRuntime = FindSceneObject<P3AreaPolicyRuntime>();
        P3CommandPollingClient commandPollingClient = FindSceneObject<P3CommandPollingClient>();
        bool valid = true;

        if (settings == null)
        {
            Debug.LogError("[P3] P3BackendSettings was not found.");
            valid = false;
        }
        else
        {
            string error;
            if (!settings.TryValidate(out error))
            {
                Debug.LogError("[P3] Settings validation failed: " + error);
                valid = false;
            }

            if (settings.authenticationMode == P3AuthenticationMode.ApiKeyHeader &&
                string.IsNullOrWhiteSpace(settings.apiKey))
            {
                Debug.LogWarning(
                    "[P3] Local Command v1.0 contract requires X-API-Key. " +
                    "Enter the local development key in P3BackendSettings. Do not commit real keys."
                );
            }
        }

        if (status == null)
        {
            Debug.LogError("[P3] P3TransmissionStatus was not found.");
            valid = false;
        }

        if (eventSender == null)
        {
            Debug.LogError("[P3] P3EventHttpSender was not found.");
            valid = false;
        }
        else
        {
            eventSender.ResolveReferences();
            if (eventSender.eventPublisher == null)
            {
                Debug.LogError("[P3] P2SimulationEventPublisher is not assigned.");
                valid = false;
            }
            if (eventSender.contractValidator == null)
            {
                Debug.LogError("[P3] P2EventContractValidator is not assigned.");
                valid = false;
            }
        }

        if (snapshotSender == null)
        {
            Debug.LogError("[P3] P3SnapshotHttpSender was not found.");
            valid = false;
        }
        else
        {
            snapshotSender.ResolveReferences();
            if (snapshotSender.snapshotWriter == null)
            {
                Debug.LogError("[P3] P2LocalSnapshotWriter is not assigned.");
                valid = false;
            }
            if (snapshotSender.contractValidator == null)
            {
                Debug.LogError("[P3] P2SnapshotContractValidator is not assigned.");
                valid = false;
            }
        }

        if (healthChecker == null)
        {
            Debug.LogError("[P3] P3BackendHealthChecker was not found.");
            valid = false;
        }

        if (commandStatus == null || commandValidator == null || areaPolicyRuntime == null || commandPollingClient == null)
        {
            Debug.LogError("[P3] One or more Command v1.0 components are missing.");
            valid = false;
        }
        else
        {
            commandPollingClient.ResolveReferences();
            if (commandPollingClient.eventPublisher == null)
            {
                Debug.LogError("[P3] P2SimulationEventPublisher is not assigned to Command Polling.");
                valid = false;
            }
        }

        if (valid)
        {
            Debug.Log(
                "[P3] Backend transmission setup validation passed. " +
                "Confirmed Snapshot/Event and Command v1.0 settings are valid on the Unity side."
            );
        }
    }

    [MenuItem("Tools/Smart Parking/P3/Flush Pending Data Now")]
    public static void FlushPendingDataNow()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[P3] Flush Pending Data Now is available during Play only.");
            return;
        }

        P3EventHttpSender eventSender = FindSceneObject<P3EventHttpSender>();
        P3SnapshotHttpSender snapshotSender = FindSceneObject<P3SnapshotHttpSender>();

        if (eventSender != null)
        {
            eventSender.RequestImmediateFlush();
        }

        if (snapshotSender != null)
        {
            snapshotSender.RequestImmediateFlush();
        }

        if (eventSender == null && snapshotSender == null)
        {
            Debug.LogError("[P3] No P3 sender was found.");
        }
    }

    [MenuItem("Tools/Smart Parking/P3/Poll Commands Now")]
    public static void PollCommandsNow()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[P3] Poll Commands Now is available during Play only.");
            return;
        }

        P3CommandPollingClient client = FindSceneObject<P3CommandPollingClient>();
        if (client == null)
        {
            Debug.LogError("[P3] P3CommandPollingClient was not found.");
            return;
        }

        client.PollNow();
    }

    [MenuItem("Tools/Smart Parking/P3/Check Backend Health Now")]
    public static void CheckBackendHealthNow()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("[P3] Check Backend Health Now is available during Play only.");
            return;
        }

        P3BackendHealthChecker checker = FindSceneObject<P3BackendHealthChecker>();
        if (checker == null)
        {
            Debug.LogError("[P3] P3BackendHealthChecker was not found.");
            return;
        }

        checker.RequestHealthCheck();
    }

    [MenuItem("Tools/Smart Parking/P3/Requeue Failed Data")]
    public static void RequeueFailedData()
    {
        P3BackendSettings settings = FindSceneObject<P3BackendSettings>();
        if (settings == null)
        {
            Debug.LogError("[P3] P3BackendSettings was not found.");
            return;
        }

        int eventCount = P3PendingFileStore.RequeueJsonFiles(
            settings.FailedEventPath,
            settings.PendingEventPath
        );
        int snapshotCount = P3PendingFileStore.RequeueJsonFiles(
            settings.FailedSnapshotPath,
            settings.PendingSnapshotPath
        );

        Debug.Log(
            "[P3] Requeued failed data. Events=" + eventCount +
            ", Snapshots=" + snapshotCount +
            ". During Play, use Flush Pending Data Now to send immediately."
        );
    }

    [MenuItem("Tools/Smart Parking/P3/Open Pending Data Folder")]
    public static void OpenPendingDataFolder()
    {
        P3BackendSettings settings = FindSceneObject<P3BackendSettings>();
        string path = settings != null
            ? settings.PendingRootPath
            : Path.Combine(Application.persistentDataPath, "P3Pending");
        Directory.CreateDirectory(path);
        Application.OpenURL("file:///" + path.Replace('\\', '/'));
    }

    [MenuItem("Tools/Smart Parking/P3/Open Failed Data Folder")]
    public static void OpenFailedDataFolder()
    {
        P3BackendSettings settings = FindSceneObject<P3BackendSettings>();
        string path = settings != null
            ? settings.FailedRootPath
            : Path.Combine(Application.persistentDataPath, "P3Failed");
        Directory.CreateDirectory(path);
        Application.OpenURL("file:///" + path.Replace('\\', '/'));
    }

    private static GameObject FindRootInScene(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root != null && root.name == objectName)
            {
                return root;
            }
        }

        return null;
    }

    private static T GetOrAddComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        if (component == null)
        {
            component = Undo.AddComponent<T>(target);
        }

        return component;
    }

    private static T FindSceneObject<T>() where T : Object
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindFirstObjectByType<T>();
#else
        return Object.FindObjectOfType<T>();
#endif
    }
}
#endif
