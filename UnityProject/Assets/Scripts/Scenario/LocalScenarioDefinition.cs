using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "LocalScenarioDefinition",
    menuName = "Smart Parking/P1/Local Scenario Definition"
)]
public class LocalScenarioDefinition : ScriptableObject
{
    [Header("Contract Identity")]
    public string schemaVersion = "1.0";
    public string scenarioId = "scenario-concert-rain";
    public string scenarioVersion = "p1-local-1";
    public string facilityId = "facility-main";
    public string mapVersion = "2026-07-16";

    [Header("Description")]
    public string scenarioName = "P1 Local Scenario";

    [TextArea(2, 5)]
    public string description = "Backend Commandを使わず、Unityローカル設定で外部要因を再現するP1用シナリオ。";

    public string timeOfDay = "daytime";

    [Min(0.1f)]
    public float durationSeconds = 14400f;

    [Header("Reproducibility")]
    public string randomSeedPolicy = "fixed_per_repetition";
    public int randomSeed = 12345;

    [Header("Arrival Rate")]
    public List<ArrivalRateSegment> arrivalRateSegments = new List<ArrivalRateSegment>();

    [Header("Scenario Factors")]
    public List<ScenarioFactorDefinition> scenarioFactors = new List<ScenarioFactorDefinition>();

    private void OnValidate()
    {
        durationSeconds = Mathf.Max(0.1f, durationSeconds);

        if (arrivalRateSegments == null)
        {
            arrivalRateSegments = new List<ArrivalRateSegment>();
        }

        if (scenarioFactors == null)
        {
            scenarioFactors = new List<ScenarioFactorDefinition>();
        }
    }
}
