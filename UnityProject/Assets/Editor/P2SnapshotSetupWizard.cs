#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class P2SnapshotSetupWizard
{
    private const string RootObjectName = "P2SnapshotSystem";

    [MenuItem("Tools/Smart Parking/P2/Create or Update Snapshot Setup")]
    public static void CreateOrUpdateSnapshotSetup()
    {
        Scene scene = SceneManager.GetActiveScene();

        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[P2Snapshot] No loaded active Scene was found.");
            return;
        }

        GameObject root = FindRootInScene(scene, RootObjectName);

        if (root == null)
        {
            root = new GameObject(RootObjectName);
            Undo.RegisterCreatedObjectUndo(root, "Create P2 Snapshot System");
            SceneManager.MoveGameObjectToScene(root, scene);
        }

        P2SimulationSnapshotBuilder builder = GetOrAddComponent<P2SimulationSnapshotBuilder>(root);
        P2SnapshotContractValidator validator = GetOrAddComponent<P2SnapshotContractValidator>(root);
        P2LocalSnapshotWriter writer = GetOrAddComponent<P2LocalSnapshotWriter>(root);

        ScenarioFactorRuntime scenarioRuntime = FindSceneObject<ScenarioFactorRuntime>();
        SimulationClock clock = scenarioRuntime != null
            ? scenarioRuntime.Clock
            : FindSceneObject<SimulationClock>();
        ScenarioTargetBinding targetBinding = scenarioRuntime != null
            ? scenarioRuntime.targetBinding
            : FindSceneObject<ScenarioTargetBinding>();
        ParkingLotManager parkingLotManager = FindSceneObject<ParkingLotManager>();
        VehicleSpawnManager vehicleSpawnManager = FindSceneObject<VehicleSpawnManager>();

        Undo.RecordObject(builder, "Configure P2 Snapshot Builder");
        builder.autoFindReferences = true;
        builder.scenarioRuntime = scenarioRuntime;
        builder.simulationClock = clock;
        builder.scenarioDefinition = scenarioRuntime != null
            ? scenarioRuntime.Definition
            : (clock != null ? clock.scenarioDefinition : null);
        builder.targetBinding = targetBinding;
        builder.parkingLotManager = parkingLotManager;
        builder.vehicleSpawnManager = vehicleSpawnManager;
        builder.includeParkingSlots = true;
        builder.includeVehicles = true;
        builder.includeAccessPoints = true;
        builder.includeScenarioFactorEffects = true;
        builder.timeDecimalPlaces = 3;
        builder.scalarDecimalPlaces = 4;
        builder.vectorDecimalPlaces = 3;

        Undo.RecordObject(validator, "Configure P2 Snapshot Validator");
        validator.requireVehicles = false;
        validator.requireParkingSlots = true;
        validator.requireAreas = true;
        validator.requireAccessPoints = true;
        validator.warnWhenActiveCountDiffersFromDiscoveredCount = true;

        Undo.RecordObject(writer, "Configure P2 Snapshot Writer");
        writer.snapshotBuilder = builder;
        writer.contractValidator = validator;
        writer.simulationClock = clock;
        writer.writeOnStart = true;
        writer.writeRepeatedly = true;
        writer.intervalMode = P2SnapshotIntervalMode.SimulationSeconds;
        writer.writeIntervalSeconds = 60f;
        writer.stopAfterScenarioCompleted = true;
        writer.outputDirectoryName = "P2Snapshots";
        writer.fileNamePrefix = "snapshot";
        writer.prettyPrint = true;
        writer.keepSequenceFiles = true;
        writer.writeLatestCopy = true;
        writer.writeWhenValidationFails = false;

        EditorUtility.SetDirty(builder);
        EditorUtility.SetDirty(validator);
        EditorUtility.SetDirty(writer);
        EditorUtility.SetDirty(root);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;

        Debug.Log(
            "[P2Snapshot] Snapshot setup was created or updated. " +
            "Play the Scene to write JSON files to Application.persistentDataPath/P2Snapshots."
        );
    }

    [MenuItem("Tools/Smart Parking/P2/Validate Snapshot Setup")]
    public static void ValidateSnapshotSetup()
    {
        P2SimulationSnapshotBuilder builder = FindSceneObject<P2SimulationSnapshotBuilder>();
        P2SnapshotContractValidator validator = FindSceneObject<P2SnapshotContractValidator>();
        P2LocalSnapshotWriter writer = FindSceneObject<P2LocalSnapshotWriter>();

        bool valid = true;

        if (builder == null)
        {
            Debug.LogError("[P2Snapshot] P2SimulationSnapshotBuilder was not found.");
            valid = false;
        }

        if (validator == null)
        {
            Debug.LogError("[P2Snapshot] P2SnapshotContractValidator was not found.");
            valid = false;
        }

        if (writer == null)
        {
            Debug.LogError("[P2Snapshot] P2LocalSnapshotWriter was not found.");
            valid = false;
        }

        if (builder != null)
        {
            builder.ResolveReferences();

            if (builder.scenarioRuntime == null)
            {
                Debug.LogError("[P2Snapshot] ScenarioFactorRuntime is not assigned.");
                valid = false;
            }

            if (builder.simulationClock == null)
            {
                Debug.LogError("[P2Snapshot] SimulationClock is not assigned.");
                valid = false;
            }

            if (builder.parkingLotManager == null)
            {
                Debug.LogError("[P2Snapshot] ParkingLotManager is not assigned.");
                valid = false;
            }

            if (builder.vehicleSpawnManager == null)
            {
                Debug.LogError("[P2Snapshot] VehicleSpawnManager is not assigned.");
                valid = false;
            }
        }

        if (writer != null && writer.snapshotBuilder == null)
        {
            Debug.LogError("[P2Snapshot] Writer has no Snapshot Builder reference.");
            valid = false;
        }

        if (valid)
        {
            Debug.Log("[P2Snapshot] Snapshot setup validation passed.");
        }
    }

    [MenuItem("Tools/Smart Parking/P2/Write Snapshot Now")]
    public static void WriteSnapshotNow()
    {
        P2LocalSnapshotWriter writer = FindSceneObject<P2LocalSnapshotWriter>();

        if (writer == null)
        {
            Debug.LogError(
                "[P2Snapshot] P2LocalSnapshotWriter was not found. " +
                "Run Create or Update Snapshot Setup first."
            );
            return;
        }

        writer.WriteSnapshotNow();
    }

    [MenuItem("Tools/Smart Parking/P2/Open Snapshot Output Folder")]
    public static void OpenSnapshotOutputFolder()
    {
        P2LocalSnapshotWriter writer = FindSceneObject<P2LocalSnapshotWriter>();

        if (writer == null)
        {
            Debug.LogError(
                "[P2Snapshot] P2LocalSnapshotWriter was not found. " +
                "Run Create or Update Snapshot Setup first."
            );
            return;
        }

        writer.OpenSnapshotOutputFolder();
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
