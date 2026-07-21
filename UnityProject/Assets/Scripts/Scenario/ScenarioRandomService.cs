using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-950)]
public class ScenarioRandomService : MonoBehaviour
{
    public static ScenarioRandomService Instance { get; private set; }

    [Header("Seed Source")]
    public LocalScenarioDefinition scenarioDefinition;
    public bool useSeedOverride;
    public int seedOverride = 12345;
    public bool initializeOnAwake = true;

    [Header("Runtime (Read Only)")]
    [SerializeField]
    private int currentSeed;

    [SerializeField]
    private long drawCount;

    private System.Random random;

    public int CurrentSeed => currentSeed;
    public long DrawCount => drawCount;
    public bool IsInitialized => random != null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"{name}: ScenarioRandomService が複数存在します。先に初期化されたものを使用します。");
        }
        else
        {
            Instance = this;
        }

        if (initializeOnAwake)
        {
            InitializeFromDefinition();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    [ContextMenu("Reset Random Sequence")]
    public void InitializeFromDefinition()
    {
        int seed = useSeedOverride
            ? seedOverride
            : scenarioDefinition != null
                ? scenarioDefinition.randomSeed
                : seedOverride;

        Initialize(seed);
    }

    public void Initialize(int seed)
    {
        currentSeed = seed;
        drawCount = 0;
        random = new System.Random(seed);
    }

    public int Range(int minInclusive, int maxExclusive)
    {
        EnsureInitialized();

        if (maxExclusive <= minInclusive)
        {
            return minInclusive;
        }

        drawCount++;
        return random.Next(minInclusive, maxExclusive);
    }

    public float Range(float minInclusive, float maxInclusive)
    {
        EnsureInitialized();

        if (maxInclusive <= minInclusive)
        {
            return minInclusive;
        }

        return Mathf.Lerp(minInclusive, maxInclusive, NextUnitFloat());
    }

    public float NextUnitFloat()
    {
        EnsureInitialized();
        drawCount++;
        return (float)random.NextDouble();
    }

    public int ChooseWeightedIndex(IList<float> weights)
    {
        if (weights == null || weights.Count == 0)
        {
            return -1;
        }

        float total = 0f;

        for (int i = 0; i < weights.Count; i++)
        {
            total += Mathf.Max(0f, weights[i]);
        }

        if (total <= 0f)
        {
            return Range(0, weights.Count);
        }

        float value = Range(0f, total);
        float cumulative = 0f;

        for (int i = 0; i < weights.Count; i++)
        {
            cumulative += Mathf.Max(0f, weights[i]);

            if (value <= cumulative)
            {
                return i;
            }
        }

        return weights.Count - 1;
    }

    private void EnsureInitialized()
    {
        if (random == null)
        {
            InitializeFromDefinition();
        }
    }
}
