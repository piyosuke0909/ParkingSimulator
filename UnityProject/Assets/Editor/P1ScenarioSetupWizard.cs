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
            AssetDatabase.CreateAsset(definition, ScenarioAssetPath);
            createdDefinition = true;
        }

        ApplyFormalExtendedP1Settings(definition);

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
        runtime.requireActiveArrivalRateSegment = false;

        VehicleSpawnManager spawnManager = UnityEngine.Object.FindObjectOfType<VehicleSpawnManager>();
        ParkingLotManager parkingLotManager = UnityEngine.Object.FindObjectOfType<ParkingLotManager>();

        if (spawnManager != null)
        {
            spawnManager.useScenarioSystem = true;
            spawnManager.useScenarioAreaPreference = true;
            spawnManager.scenarioRuntime = runtime;
            spawnManager.scenarioRandomService = randomService;
            spawnManager.scenarioRunLogger = logger;
            spawnManager.maxConcurrentVehicles = 60;
            PopulateAccessPointBindings(targetBinding, spawnManager);
            EditorUtility.SetDirty(spawnManager);
        }
        else
        {
            Debug.LogWarning("P1 Setup: Scene内にVehicleSpawnManagerが見つかりませんでした。");
        }

        if (parkingLotManager != null)
        {
            parkingLotManager.RegisterAllSlots();
            PopulateAreaBindings(targetBinding, parkingLotManager);
            EditorUtility.SetDirty(parkingLotManager);
        }
        else
        {
            Debug.LogWarning("P1 Setup: Scene内にParkingLotManagerが見つかりませんでした。");
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
            ? "P1 Scenario Systemと正式設定のScenario Assetを作成しました。"
            : "既存P1 Scenarioを拡張仕様へ更新しました。";

        message +=
            "\n\n時間帯流入設定: 1800～5400秒 / 3台毎分 / Poisson / 施設全体" +
            "\n区間外: VehicleSpawnManager.spawnIntervalへフォールバック" +
            "\n同時存在台数上限: 60台" +
            "\n\nArea効果の対象と倍率は資料で未指定です。" +
            "P1_LocalScenarioのEffectへ targetType=Area、targetId=area-a等を追加してください。";

        EditorUtility.DisplayDialog("P1 Scenario Setup", message, "OK");
    }

    [MenuItem("Tools/Smart Parking/P1/Apply Formal Extended Settings")]
    public static void ApplyFormalExtendedSettingsOnly()
    {
        LocalScenarioDefinition definition = AssetDatabase.LoadAssetAtPath<LocalScenarioDefinition>(ScenarioAssetPath);

        if (definition == null)
        {
            EditorUtility.DisplayDialog(
                "P1 Formal Settings",
                "P1_LocalScenario.assetがありません。先にCreate or Update Scenario Setupを実行してください。",
                "OK"
            );
            return;
        }

        ApplyFormalExtendedP1Settings(definition);
        EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("P1 Formal Settings", "正式設定を適用しました。", "OK");
    }

    [MenuItem("Tools/Smart Parking/P1/Validate Scenario Setup")]
    public static void ValidateScenarioSetup()
    {
        List<string> problems = new List<string>();

        ScenarioFactorRuntime runtime = UnityEngine.Object.FindObjectOfType<ScenarioFactorRuntime>();
        VehicleSpawnManager spawnManager = UnityEngine.Object.FindObjectOfType<VehicleSpawnManager>();
        ParkingLotManager parkingLotManager = UnityEngine.Object.FindObjectOfType<ParkingLotManager>();

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
            else
            {
                ValidateFormalArrivalSegment(runtime.scenarioDefinition, problems);
                ValidateFactorIds(runtime.scenarioDefinition, problems);
                ValidateTargetEffects(runtime, problems);
            }

            if (runtime.simulationClock == null)
            {
                problems.Add("SimulationClockが未設定です。");
            }

            if (runtime.randomService == null)
            {
                problems.Add("ScenarioRandomServiceが未設定です。");
            }

            if (runtime.requireActiveArrivalRateSegment)
            {
                problems.Add("requireActiveArrivalRateSegmentがONです。区間外フォールバック仕様ではOFFにしてください。");
            }

            if (runtime.targetBinding == null)
            {
                problems.Add("ScenarioTargetBindingが未設定です。");
            }
        }

        if (spawnManager == null)
        {
            problems.Add("VehicleSpawnManagerがSceneにありません。");
        }
        else
        {
            if (!spawnManager.useScenarioSystem)
            {
                problems.Add("VehicleSpawnManager.useScenarioSystemがOFFです。");
            }

            if (!spawnManager.useScenarioAreaPreference)
            {
                problems.Add("VehicleSpawnManager.useScenarioAreaPreferenceがOFFです。");
            }

            if (spawnManager.maxConcurrentVehicles != 60)
            {
                problems.Add($"maxConcurrentVehiclesが正式値60ではありません: {spawnManager.maxConcurrentVehicles}");
            }
        }

        if (parkingLotManager == null)
        {
            problems.Add("ParkingLotManagerがSceneにありません。");
        }
        else if (parkingLotManager.GetAvailableAreaIdsWithAccessWaypoint().Count == 0)
        {
            problems.Add("選択可能な駐車エリアがありません。");
        }

        if (problems.Count == 0)
        {
            EditorUtility.DisplayDialog("P1 Validation", "拡張P1の必須設定に問題はありません。", "OK");
            Debug.Log("P1 Extended Validation: OK");
            return;
        }

        foreach (string problem in problems)
        {
            Debug.LogWarning("P1 Extended Validation: " + problem);
        }

        EditorUtility.DisplayDialog(
            "P1 Validation",
            $"{problems.Count}件の確認事項があります。Consoleを確認してください。",
            "OK"
        );
    }

    private static void ApplyFormalExtendedP1Settings(LocalScenarioDefinition definition)
    {
        definition.schemaVersion = "1.0";
        definition.scenarioId = "scenario-concert-rain";
        definition.scenarioVersion = "p1-local-2";
        definition.facilityId = "facility-main";
        definition.mapVersion = "2026-07-16";
        definition.scenarioName = "P1 Local External Factors";
        definition.description =
            "雨・猛暑・コンサート、エリア需要、Poisson流入をUnityローカルで再現する。" +
            "Backend Commandは使用しない。";
        definition.timeOfDay = "daytime";
        definition.durationSeconds = 14400f;
        definition.randomSeedPolicy = "fixed_per_repetition";

        if (definition.randomSeed == 0)
        {
            definition.randomSeed = 12345;
        }

        definition.arrivalRateSegments = new List<ArrivalRateSegment>
        {
            new ArrivalRateSegment
            {
                enabled = true,
                arrivalRateSegmentId = "arrival-global-1800-5400",
                accessPointId = string.Empty,
                startSimulationTimeSeconds = 1800f,
                endSimulationTimeSeconds = 5400f,
                vehiclesPerMinute = 3f,
                distribution = ArrivalDistribution.Poisson
            }
        };

        if (definition.scenarioFactors == null)
        {
            definition.scenarioFactors = new List<ScenarioFactorDefinition>();
        }

        ScenarioFactorDefinition concert = EnsureFactor(
            definition,
            "factor-concert",
            ScenarioFactorCategory.ScheduledEvent,
            "concert",
            "コンサート",
            1800f,
            10800f,
            1f,
            InformationAvailability.Scheduled
        );

        EnsureEffect(
            concert,
            "effect-concert-arrival-rate",
            FactorEffectTargetType.Facility,
            string.Empty,
            ScenarioMetrics.ArrivalRate,
            FactorEffectOperation.Multiply,
            1.8f
        );

        EnsureEffect(
            concert,
            "effect-concert-event-entrance",
            FactorEffectTargetType.AccessPoint,
            "entrance-event",
            ScenarioMetrics.ParkingPreferenceWeight,
            FactorEffectOperation.Multiply,
            2f
        );

        ScenarioFactorDefinition rain = EnsureFactor(
            definition,
            "factor-heavy-rain",
            ScenarioFactorCategory.Weather,
            "heavy_rain",
            "強い雨",
            3600f,
            9000f,
            0.8f,
            InformationAvailability.Forecasted
        );

        EnsureEffect(
            rain,
            "effect-heavy-rain-speed",
            FactorEffectTargetType.VehiclePopulation,
            string.Empty,
            ScenarioMetrics.Speed,
            FactorEffectOperation.Multiply,
            0.85f
        );

        EnsureEffect(
            rain,
            "effect-heavy-rain-covered-entrance",
            FactorEffectTargetType.AccessPoint,
            "entrance-covered",
            ScenarioMetrics.ParkingPreferenceWeight,
            FactorEffectOperation.Multiply,
            1.4f
        );

        ScenarioFactorDefinition heat = EnsureFactor(
            definition,
            "factor-extreme-heat",
            ScenarioFactorCategory.Temperature,
            "extreme_heat",
            "猛暑",
            0f,
            14400f,
            1f,
            InformationAvailability.Forecasted
        );

        EnsureEffect(
            heat,
            "effect-extreme-heat-main-entrance",
            FactorEffectTargetType.AccessPoint,
            "entrance-main",
            ScenarioMetrics.ParkingPreferenceWeight,
            FactorEffectOperation.Multiply,
            1.6f
        );
    }

    private static ScenarioFactorDefinition EnsureFactor(
        LocalScenarioDefinition definition,
        string id,
        ScenarioFactorCategory category,
        string code,
        string label,
        float start,
        float end,
        float intensity,
        InformationAvailability availability)
    {
        ScenarioFactorDefinition factor = definition.scenarioFactors.Find(item =>
            item != null && string.Equals(item.scenarioFactorId, id, StringComparison.Ordinal));

        if (factor == null)
        {
            factor = new ScenarioFactorDefinition();
            definition.scenarioFactors.Add(factor);
        }

        factor.enabled = true;
        factor.scenarioFactorId = id;
        factor.category = category;
        factor.factorCode = code;
        factor.label = label;
        factor.startSimulationTimeSeconds = start;
        factor.endSimulationTimeSeconds = end;
        factor.intensity = intensity;
        factor.informationAvailability = availability;
        factor.hasKnownFromSimulationTimeSeconds = true;
        factor.knownFromSimulationTimeSeconds = 0f;

        if (factor.effects == null)
        {
            factor.effects = new List<ScenarioFactorEffect>();
        }

        return factor;
    }

    private static void EnsureEffect(
        ScenarioFactorDefinition factor,
        string id,
        FactorEffectTargetType targetType,
        string targetId,
        string metric,
        FactorEffectOperation operation,
        float value)
    {
        ScenarioFactorEffect effect = factor.effects.Find(item =>
            item != null && string.Equals(item.scenarioFactorEffectId, id, StringComparison.Ordinal));

        if (effect == null)
        {
            effect = new ScenarioFactorEffect();
            factor.effects.Add(effect);
        }

        effect.enabled = true;
        effect.scenarioFactorEffectId = id;
        effect.targetType = targetType;
        effect.targetId = targetId;
        effect.metric = metric;
        effect.operation = operation;
        effect.value = value;
        effect.unit = "ratio";
    }

    private static void PopulateAccessPointBindings(
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

                if (!ContainsAccessBinding(targetBinding, canonicalId))
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

        AddAccessPlaceholder(targetBinding, "entrance-event");
        AddAccessPlaceholder(targetBinding, "entrance-covered");
        AddAccessPlaceholder(targetBinding, "entrance-main");
    }

    private static void PopulateAreaBindings(
        ScenarioTargetBinding targetBinding,
        ParkingLotManager parkingLotManager)
    {
        if (targetBinding.areaBindings == null)
        {
            targetBinding.areaBindings = new List<ScenarioAreaBinding>();
        }

        HashSet<string> areaIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (ParkingSlot slot in parkingLotManager.allSlots)
        {
            if (slot == null || string.IsNullOrEmpty(slot.areaId) || !areaIds.Add(slot.areaId))
            {
                continue;
            }

            string canonicalId = NormalizeAreaId(slot.areaId);

            if (ContainsAreaBinding(targetBinding, canonicalId))
            {
                continue;
            }

            targetBinding.areaBindings.Add(new ScenarioAreaBinding
            {
                canonicalAreaId = canonicalId,
                sceneAreaId = slot.areaId,
                primaryForLogging = true
            });
        }
    }

    private static void AddAccessPlaceholder(ScenarioTargetBinding targetBinding, string canonicalId)
    {
        if (ContainsAccessBinding(targetBinding, canonicalId))
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

    private static bool ContainsAccessBinding(ScenarioTargetBinding targetBinding, string canonicalId)
    {
        return targetBinding.accessPointBindings.Exists(binding =>
            binding != null &&
            string.Equals(binding.canonicalAccessPointId, canonicalId, StringComparison.Ordinal));
    }

    private static bool ContainsAreaBinding(ScenarioTargetBinding targetBinding, string canonicalId)
    {
        return targetBinding.areaBindings.Exists(binding =>
            binding != null &&
            string.Equals(binding.canonicalAreaId, canonicalId, StringComparison.Ordinal));
    }

    private static string DeriveCanonicalAccessPointId(string pairName)
    {
        string lower = pairName.ToLowerInvariant();

        if (lower.Contains("west")) return "entrance-west";
        if (lower.Contains("east")) return "entrance-east";
        if (lower.Contains("north")) return "entrance-north";
        if (lower.Contains("south")) return "entrance-south";

        return "entrance-" + lower.Replace('_', '-').Replace(' ', '-');
    }

    private static string NormalizeAreaId(string areaId)
    {
        string normalized = areaId.Trim().ToLowerInvariant()
            .Replace('_', '-')
            .Replace(' ', '-');

        return normalized.StartsWith("area-", StringComparison.Ordinal)
            ? normalized
            : "area-" + normalized;
    }

    private static void ValidateFormalArrivalSegment(
        LocalScenarioDefinition definition,
        List<string> problems)
    {
        if (definition.arrivalRateSegments == null || definition.arrivalRateSegments.Count != 1)
        {
            problems.Add("正式設定ではArrivalRateSegmentを1件にしてください。");
            return;
        }

        ArrivalRateSegment segment = definition.arrivalRateSegments[0];

        if (segment == null ||
            !Mathf.Approximately(segment.startSimulationTimeSeconds, 1800f) ||
            !Mathf.Approximately(segment.endSimulationTimeSeconds, 5400f) ||
            !Mathf.Approximately(segment.vehiclesPerMinute, 3f) ||
            segment.distribution != ArrivalDistribution.Poisson ||
            !string.IsNullOrEmpty(segment.accessPointId))
        {
            problems.Add("ArrivalRateSegmentが正式値（1800～5400秒、3台/分、Poisson、施設全体）ではありません。");
        }
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

    private static void ValidateTargetEffects(
        ScenarioFactorRuntime runtime,
        List<string> problems)
    {
        if (runtime.scenarioDefinition == null ||
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
                if (effect == null || !effect.enabled)
                {
                    continue;
                }

                if (effect.targetType == FactorEffectTargetType.AccessPoint &&
                    !runtime.targetBinding.HasResolvedAccessPoint(effect.targetId))
                {
                    problems.Add($"AccessPoint効果の割当が未確定です: {effect.targetId}");
                }

                if (effect.targetType == FactorEffectTargetType.Area &&
                    !runtime.targetBinding.HasResolvedArea(effect.targetId))
                {
                    problems.Add($"Area効果の割当が未確定です: {effect.targetId}");
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
