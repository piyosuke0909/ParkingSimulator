#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class P1ScenarioSetupWizard
{
    private const string ScenarioFolder = "Assets/Scenario";
    private const string ScenarioAssetPath = ScenarioFolder + "/P1_LocalScenario.asset";
    private const string SystemObjectName = "P1ScenarioSystem";

    [MenuItem("Tools/Smart Parking/P1/Create or Update Scenario Setup")]
    public static void CreateOrUpdateScenarioSetup()
    {
        EnsureFolder(ScenarioFolder);

        LocalScenarioDefinition definition = AssetDatabase.LoadAssetAtPath<LocalScenarioDefinition>(ScenarioAssetPath);
        bool createdDefinition = false;

        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<LocalScenarioDefinition>();
            PopulateReferenceScenario(definition);
            AssetDatabase.CreateAsset(definition, ScenarioAssetPath);
            createdDefinition = true;
        }

        GameObject systemObject = GameObject.Find(SystemObjectName);

        if (systemObject == null)
        {
            systemObject = new GameObject(SystemObjectName);
            Undo.RegisterCreatedObjectUndo(systemObject, "Create P1 Scenario System");
        }

        SimulationClock clock = GetOrAddComponent<SimulationClock>(systemObject);
        ScenarioRandomService randomService = GetOrAddComponent<ScenarioRandomService>(systemObject);
        ScenarioTargetBinding targetBinding = GetOrAddComponent<ScenarioTargetBinding>(systemObject);
        ScenarioRunLogger logger = GetOrAddComponent<ScenarioRunLogger>(systemObject);
        ScenarioFactorRuntime runtime = GetOrAddComponent<ScenarioFactorRuntime>(systemObject);

        clock.scenarioDefinition = definition;
        randomService.scenarioDefinition = definition;
        runtime.scenarioDefinition = definition;
        runtime.simulationClock = clock;
        runtime.randomService = randomService;
        runtime.targetBinding = targetBinding;
        runtime.runLogger = logger;

        VehicleSpawnManager spawnManager = UnityEngine.Object.FindObjectOfType<VehicleSpawnManager>();

        if (spawnManager != null)
        {
            spawnManager.useScenarioSystem = true;
            spawnManager.scenarioRuntime = runtime;
            spawnManager.scenarioRandomService = randomService;
            spawnManager.scenarioRunLogger = logger;
            PopulateSceneBindings(targetBinding, spawnManager);
            EditorUtility.SetDirty(spawnManager);
        }
        else
        {
            Debug.LogWarning("P1 Setup: Scene内にVehicleSpawnManagerが見つかりませんでした。後から参照を設定してください。");
        }

        EditorUtility.SetDirty(systemObject);
        EditorUtility.SetDirty(clock);
        EditorUtility.SetDirty(randomService);
        EditorUtility.SetDirty(targetBinding);
        EditorUtility.SetDirty(logger);
        EditorUtility.SetDirty(runtime);
        EditorUtility.SetDirty(definition);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = systemObject;

        string message = createdDefinition
            ? "P1 Scenario Systemと基準シナリオAssetを作成しました。"
            : "P1 Scenario Systemを既存シナリオAssetへ接続しました。";

        message += "\n\nScenarioTargetBindingの entrance-event / entrance-covered / entrance-main を実際のpairNameへ割り当ててください。";
        EditorUtility.DisplayDialog("P1 Scenario Setup", message, "OK");
    }

    [MenuItem("Tools/Smart Parking/P1/Validate Scenario Setup")]
    public static void ValidateScenarioSetup()
    {
        List<string> problems = new List<string>();

        ScenarioFactorRuntime runtime = UnityEngine.Object.FindObjectOfType<ScenarioFactorRuntime>();
        VehicleSpawnManager spawnManager = UnityEngine.Object.FindObjectOfType<VehicleSpawnManager>();

        if (runtime == null)
        {
            problems.Add("ScenarioFactorRuntimeがSceneにありません。");
        }
        else
        {
            if (runtime.scenarioDefinition == null)
            {
                problems.Add("ScenarioFactorRuntime.scenarioDefinitionが未設定です。");
            }

            if (runtime.simulationClock == null)
            {
                problems.Add("SimulationClockが未設定です。");
            }

            if (runtime.randomService == null)
            {
                problems.Add("ScenarioRandomServiceが未設定です。");
            }

            if (runtime.targetBinding == null)
            {
                problems.Add("ScenarioTargetBindingが未設定です。");
            }

            ValidateFactorIds(runtime.scenarioDefinition, problems);
            ValidateAccessPointEffects(runtime, spawnManager, problems);
        }

        if (spawnManager == null)
        {
            problems.Add("VehicleSpawnManagerがSceneにありません。");
        }
        else if (!spawnManager.useScenarioSystem)
        {
            problems.Add("VehicleSpawnManager.useScenarioSystemがOFFです。");
        }

        if (problems.Count == 0)
        {
            EditorUtility.DisplayDialog("P1 Validation", "P1の必須参照に問題はありません。", "OK");
            Debug.Log("P1 Validation: OK");
            return;
        }

        foreach (string problem in problems)
        {
            Debug.LogWarning("P1 Validation: " + problem);
        }

        EditorUtility.DisplayDialog(
            "P1 Validation",
            $"{problems.Count}件の確認事項があります。Consoleを確認してください。",
            "OK"
        );
    }

    private static void PopulateReferenceScenario(LocalScenarioDefinition definition)
    {
        definition.schemaVersion = "1.0";
        definition.scenarioId = "scenario-concert-rain";
        definition.scenarioVersion = "p1-local-1";
        definition.facilityId = "facility-main";
        definition.mapVersion = "2026-07-16";
        definition.scenarioName = "P1 Local External Factors";
        definition.description = "資料v4の雨・猛暑・コンサート・時間帯をUnityローカルで再現する。Backend Commandは使用しない。";
        definition.timeOfDay = "daytime";
        definition.durationSeconds = 14400f;
        definition.randomSeedPolicy = "fixed_per_repetition";
        definition.randomSeed = 12345;

        definition.arrivalRateSegments = new List<ArrivalRateSegment>
        {
            new ArrivalRateSegment
            {
                enabled = true,
                arrivalRateSegmentId = "arrival-base-daytime",
                accessPointId = string.Empty,
                startSimulationTimeSeconds = 0f,
                endSimulationTimeSeconds = 14400f,
                vehiclesPerMinute = 12f,
                distribution = ArrivalDistribution.Fixed
            }
        };

        definition.scenarioFactors = new List<ScenarioFactorDefinition>
        {
            CreateConcertFactor(),
            CreateHeavyRainFactor(),
            CreateExtremeHeatFactor()
        };
    }

    private static ScenarioFactorDefinition CreateConcertFactor()
    {
        return new ScenarioFactorDefinition
        {
            enabled = true,
            scenarioFactorId = "factor-concert",
            category = ScenarioFactorCategory.ScheduledEvent,
            factorCode = "concert",
            label = "コンサート",
            startSimulationTimeSeconds = 1800f,
            endSimulationTimeSeconds = 10800f,
            intensity = 1f,
            informationAvailability = InformationAvailability.Scheduled,
            hasKnownFromSimulationTimeSeconds = true,
            knownFromSimulationTimeSeconds = 0f,
            effects = new List<ScenarioFactorEffect>
            {
                new ScenarioFactorEffect
                {
                    scenarioFactorEffectId = "effect-concert-arrival-rate",
                    targetType = FactorEffectTargetType.Facility,
                    targetId = string.Empty,
                    metric = ScenarioMetrics.ArrivalRate,
                    operation = FactorEffectOperation.Multiply,
                    value = 1.8f,
                    unit = "ratio"
                },
                new ScenarioFactorEffect
                {
                    scenarioFactorEffectId = "effect-concert-event-entrance",
                    targetType = FactorEffectTargetType.AccessPoint,
                    targetId = "entrance-event",
                    metric = ScenarioMetrics.ParkingPreferenceWeight,
                    operation = FactorEffectOperation.Multiply,
                    value = 2f,
                    unit = "ratio"
                }
            }
        };
    }

    private static ScenarioFactorDefinition CreateHeavyRainFactor()
    {
        return new ScenarioFactorDefinition
        {
            enabled = true,
            scenarioFactorId = "factor-heavy-rain",
            category = ScenarioFactorCategory.Weather,
            factorCode = "heavy_rain",
            label = "強い雨",
            startSimulationTimeSeconds = 3600f,
            endSimulationTimeSeconds = 9000f,
            intensity = 0.8f,
            informationAvailability = InformationAvailability.Forecasted,
            hasKnownFromSimulationTimeSeconds = true,
            knownFromSimulationTimeSeconds = 0f,
            effects = new List<ScenarioFactorEffect>
            {
                new ScenarioFactorEffect
                {
                    scenarioFactorEffectId = "effect-heavy-rain-speed",
                    targetType = FactorEffectTargetType.VehiclePopulation,
                    targetId = string.Empty,
                    metric = ScenarioMetrics.Speed,
                    operation = FactorEffectOperation.Multiply,
                    value = 0.85f,
                    unit = "ratio"
                },
                new ScenarioFactorEffect
                {
                    scenarioFactorEffectId = "effect-heavy-rain-covered-entrance",
                    targetType = FactorEffectTargetType.AccessPoint,
                    targetId = "entrance-covered",
                    metric = ScenarioMetrics.ParkingPreferenceWeight,
                    operation = FactorEffectOperation.Multiply,
                    value = 1.4f,
                    unit = "ratio"
                }
            }
        };
    }

    private static ScenarioFactorDefinition CreateExtremeHeatFactor()
    {
        return new ScenarioFactorDefinition
        {
            enabled = true,
            scenarioFactorId = "factor-extreme-heat",
            category = ScenarioFactorCategory.Temperature,
            factorCode = "extreme_heat",
            label = "猛暑",
            startSimulationTimeSeconds = 0f,
            endSimulationTimeSeconds = 14400f,
            intensity = 1f,
            informationAvailability = InformationAvailability.Forecasted,
            hasKnownFromSimulationTimeSeconds = true,
            knownFromSimulationTimeSeconds = 0f,
            effects = new List<ScenarioFactorEffect>
            {
                new ScenarioFactorEffect
                {
                    scenarioFactorEffectId = "effect-extreme-heat-main-entrance",
                    targetType = FactorEffectTargetType.AccessPoint,
                    targetId = "entrance-main",
                    metric = ScenarioMetrics.ParkingPreferenceWeight,
                    operation = FactorEffectOperation.Multiply,
                    value = 1.6f,
                    unit = "ratio"
                }
            }
        };
    }

    private static void PopulateSceneBindings(
        ScenarioTargetBinding targetBinding,
        VehicleSpawnManager spawnManager)
    {
        if (targetBinding.accessPointBindings == null)
        {
            targetBinding.accessPointBindings = new List<ScenarioAccessPointBinding>();
        }

        if (spawnManager.entranceExitPairs != null)
        {
            foreach (EntranceExitPair pair in spawnManager.entranceExitPairs)
            {
                if (pair == null || string.IsNullOrEmpty(pair.pairName))
                {
                    continue;
                }

                string canonicalId = DeriveCanonicalAccessPointId(pair.pairName);

                if (!ContainsCanonicalBinding(targetBinding, canonicalId))
                {
                    targetBinding.accessPointBindings.Add(new ScenarioAccessPointBinding
                    {
                        canonicalAccessPointId = canonicalId,
                        scenePairName = pair.pairName,
                        primaryForLogging = true
                    });
                }
            }
        }

        AddPlaceholderBinding(targetBinding, "entrance-event");
        AddPlaceholderBinding(targetBinding, "entrance-covered");
        AddPlaceholderBinding(targetBinding, "entrance-main");
    }

    private static void AddPlaceholderBinding(
        ScenarioTargetBinding targetBinding,
        string canonicalId)
    {
        if (ContainsCanonicalBinding(targetBinding, canonicalId))
        {
            return;
        }

        targetBinding.accessPointBindings.Add(new ScenarioAccessPointBinding
        {
            canonicalAccessPointId = canonicalId,
            scenePairName = string.Empty,
            primaryForLogging = false
        });
    }

    private static bool ContainsCanonicalBinding(
        ScenarioTargetBinding targetBinding,
        string canonicalId)
    {
        foreach (ScenarioAccessPointBinding binding in targetBinding.accessPointBindings)
        {
            if (binding != null &&
                string.Equals(binding.canonicalAccessPointId, canonicalId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string DeriveCanonicalAccessPointId(string pairName)
    {
        string lower = pairName.ToLowerInvariant();

        if (lower.Contains("west"))
        {
            return "entrance-west";
        }

        if (lower.Contains("east"))
        {
            return "entrance-east";
        }

        if (lower.Contains("north"))
        {
            return "entrance-north";
        }

        if (lower.Contains("south"))
        {
            return "entrance-south";
        }

        return "entrance-" + lower.Replace('_', '-').Replace(' ', '-');
    }

    private static void ValidateFactorIds(
        LocalScenarioDefinition definition,
        List<string> problems)
    {
        if (definition == null || definition.scenarioFactors == null)
        {
            return;
        }

        HashSet<string> factorIds = new HashSet<string>();
        HashSet<string> effectIds = new HashSet<string>();

        foreach (ScenarioFactorDefinition factor in definition.scenarioFactors)
        {
            if (factor == null)
            {
                continue;
            }

            if (string.IsNullOrEmpty(factor.scenarioFactorId))
            {
                problems.Add("scenarioFactorIdが空欄のFactorがあります。");
            }
            else if (!factorIds.Add(factor.scenarioFactorId))
            {
                problems.Add($"scenarioFactorIdが重複しています: {factor.scenarioFactorId}");
            }

            if (factor.endSimulationTimeSeconds <= factor.startSimulationTimeSeconds)
            {
                problems.Add($"Factorの終了時刻が開始時刻以下です: {factor.scenarioFactorId}");
            }

            if (factor.effects == null)
            {
                continue;
            }

            foreach (ScenarioFactorEffect effect in factor.effects)
            {
                if (effect == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(effect.scenarioFactorEffectId))
                {
                    problems.Add($"Effect IDが空欄です: {factor.scenarioFactorId}");
                }
                else if (!effectIds.Add(effect.scenarioFactorEffectId))
                {
                    problems.Add($"scenarioFactorEffectIdが重複しています: {effect.scenarioFactorEffectId}");
                }
            }
        }
    }

    private static void ValidateAccessPointEffects(
        ScenarioFactorRuntime runtime,
        VehicleSpawnManager spawnManager,
        List<string> problems)
    {
        if (runtime == null ||
            runtime.scenarioDefinition == null ||
            runtime.targetBinding == null ||
            runtime.scenarioDefinition.scenarioFactors == null)
        {
            return;
        }

        foreach (ScenarioFactorDefinition factor in runtime.scenarioDefinition.scenarioFactors)
        {
            if (factor == null || factor.effects == null)
            {
                continue;
            }

            foreach (ScenarioFactorEffect effect in factor.effects)
            {
                if (effect == null || effect.targetType != FactorEffectTargetType.AccessPoint)
                {
                    continue;
                }

                if (!runtime.targetBinding.HasResolvedAccessPoint(effect.targetId))
                {
                    problems.Add(
                        $"AccessPoint効果の割当が未確定です: {effect.targetId} " +
                        "→ ScenarioTargetBinding.scenePairNameを設定してください。"
                    );
                }
            }
        }
    }

    private static T GetOrAddComponent<T>(GameObject target)
        where T : Component
    {
        T component = target.GetComponent<T>();

        if (component == null)
        {
            component = Undo.AddComponent<T>(target);
        }

        return component;
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string[] parts = folderPath.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];

            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }
}
#endif
