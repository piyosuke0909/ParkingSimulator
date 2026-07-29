#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class P2EventSetupWizard
{
    private const string RootObjectName = "P2EventSystem";

    [MenuItem("Tools/Smart Parking/P2/Create or Update Event Setup")]
    public static void CreateOrUpdateEventSetup()
    {
        Scene scene = SceneManager.GetActiveScene();

        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[P2Event] No loaded active Scene was found.");
            return;
        }

        GameObject root = FindRootInScene(scene, RootObjectName);

        if (root == null)
        {
            root = new GameObject(RootObjectName);
            Undo.RegisterCreatedObjectUndo(root, "Create P2 Event System");
            SceneManager.MoveGameObjectToScene(root, scene);
        }

        P2SimulationEventPublisher publisher =
            GetOrAddComponent<P2SimulationEventPublisher>(root);
        P2EventContractValidator validator =
            GetOrAddComponent<P2EventContractValidator>(root);
        P2LocalEventWriter writer = GetOrAddComponent<P2LocalEventWriter>(root);

        ScenarioFactorRuntime scenarioRuntime = FindSceneObject<ScenarioFactorRuntime>();
        SimulationClock clock = scenarioRuntime != null
            ? scenarioRuntime.Clock
            : FindSceneObject<SimulationClock>();
        ScenarioTargetBinding targetBinding = scenarioRuntime != null
            ? scenarioRuntime.targetBinding
            : FindSceneObject<ScenarioTargetBinding>();
        VehicleSpawnManager vehicleSpawnManager = FindSceneObject<VehicleSpawnManager>();

        Undo.RecordObject(publisher, "Configure P2 Event Publisher");
        publisher.autoFindReferences = true;
        publisher.scenarioRuntime = scenarioRuntime;
        publisher.simulationClock = clock;
        publisher.scenarioDefinition = scenarioRuntime != null
            ? scenarioRuntime.Definition
            : (clock != null ? clock.scenarioDefinition : null);
        publisher.targetBinding = targetBinding;
        publisher.vehicleSpawnManager = vehicleSpawnManager;
        publisher.publishScenarioEvents = true;
        publisher.publishScenarioFactorEvents = true;
        publisher.publishVehicleStateEvents = true;
        publisher.vehicleStatePollIntervalSeconds = 0.1f;
        publisher.rewindDetectionToleranceSeconds = 0.01f;
        publisher.timeDecimalPlaces = 3;
        publisher.scalarDecimalPlaces = 4;
        publisher.vectorDecimalPlaces = 3;

        Undo.RecordObject(validator, "Configure P2 Event Validator");
        validator.requireKnownEventType = true;
        validator.requirePayloadForTypedEvents = true;

        Undo.RecordObject(writer, "Configure P2 Event Writer");
        writer.eventPublisher = publisher;
        writer.contractValidator = validator;
        writer.enableFileOutput = true;
        writer.outputDirectoryName = "P2Events";
        writer.fileNamePrefix = "events";
        writer.flushAfterEveryEvent = true;
        writer.writeWhenValidationFails = false;
        writer.logSuccess = false;
        writer.logValidationWarnings = true;
        writer.logErrors = true;

        if (vehicleSpawnManager != null)
        {
            Undo.RecordObject(vehicleSpawnManager, "Assign P2 Event Publisher");
            vehicleSpawnManager.p2EventPublisher = publisher;
            EditorUtility.SetDirty(vehicleSpawnManager);
        }

        EditorUtility.SetDirty(publisher);
        EditorUtility.SetDirty(validator);
        EditorUtility.SetDirty(writer);
        EditorUtility.SetDirty(root);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;

        Debug.Log(
            "[P2Event] Event setup was created or updated. " +
            "Play the Scene to write JSON Lines files to " +
            "Application.persistentDataPath/P2Events."
        );
    }

    [MenuItem("Tools/Smart Parking/P2/Validate Event Setup")]
    public static void ValidateEventSetup()
    {
        P2SimulationEventPublisher publisher =
            FindSceneObject<P2SimulationEventPublisher>();
        P2EventContractValidator validator = FindSceneObject<P2EventContractValidator>();
        P2LocalEventWriter writer = FindSceneObject<P2LocalEventWriter>();
        VehicleSpawnManager vehicleSpawnManager = FindSceneObject<VehicleSpawnManager>();

        bool valid = true;

        if (publisher == null)
        {
            Debug.LogError("[P2Event] P2SimulationEventPublisher was not found.");
            valid = false;
        }

        if (validator == null)
        {
            Debug.LogError("[P2Event] P2EventContractValidator was not found.");
            valid = false;
        }

        if (writer == null)
        {
            Debug.LogError("[P2Event] P2LocalEventWriter was not found.");
            valid = false;
        }

        if (publisher != null)
        {
            publisher.ResolveReferences();

            if (publisher.scenarioRuntime == null)
            {
                Debug.LogError("[P2Event] ScenarioFactorRuntime is not assigned.");
                valid = false;
            }

            if (publisher.simulationClock == null)
            {
                Debug.LogError("[P2Event] SimulationClock is not assigned.");
                valid = false;
            }

            if (publisher.vehicleSpawnManager == null)
            {
                Debug.LogError("[P2Event] VehicleSpawnManager is not assigned.");
                valid = false;
            }
        }

        if (writer != null)
        {
            writer.ResolveReferences();

            if (writer.eventPublisher == null)
            {
                Debug.LogError("[P2Event] Writer has no Event Publisher reference.");
                valid = false;
            }

            if (writer.contractValidator == null)
            {
                Debug.LogError("[P2Event] Writer has no Event Validator reference.");
                valid = false;
            }
        }

        if (vehicleSpawnManager != null &&
            publisher != null &&
            vehicleSpawnManager.p2EventPublisher != publisher)
        {
            Debug.LogError(
                "[P2Event] VehicleSpawnManager does not reference the active Event Publisher."
            );
            valid = false;
        }

        if (valid)
        {
            Debug.Log("[P2Event] Event setup validation passed.");
        }
    }

    [MenuItem("Tools/Smart Parking/P2/Open Event Output Folder")]
    public static void OpenEventOutputFolder()
    {
        P2LocalEventWriter writer = FindSceneObject<P2LocalEventWriter>();

        if (writer == null)
        {
            Debug.LogError(
                "[P2Event] P2LocalEventWriter was not found. " +
                "Run Create or Update Event Setup first."
            );
            return;
        }

        writer.OpenEventOutputFolder();
    }

    [MenuItem("Tools/Smart Parking/P2/Begin New Event Run")]
    public static void BeginNewEventRun()
    {
        P2SimulationEventPublisher publisher =
            FindSceneObject<P2SimulationEventPublisher>();

        if (publisher == null)
        {
            Debug.LogError(
                "[P2Event] P2SimulationEventPublisher was not found. " +
                "Run Create or Update Event Setup first."
            );
            return;
        }

        if (!Application.isPlaying)
        {
            Debug.LogError("[P2Event] Begin New Event Run is available during Play only.");
            return;
        }

        publisher.BeginNewRun("editor_menu");
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
