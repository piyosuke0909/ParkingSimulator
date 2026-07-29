using UnityEngine;

/// <summary>
/// 車両生成時に決定した入口・目的地情報をSnapshotへ引き継ぐための読み取り用メタデータです。
/// 車両の走行・駐車制御には使用しません。
/// </summary>
public class P2VehicleSnapshotMetadata : MonoBehaviour
{
    [Header("Snapshot Metadata (Read Only)")]
    public int spawnSequenceNumber;
    public string entranceAccessPointId;
    public string entranceScenePairName;
    public string targetAreaId;
    public string targetSceneAreaId;
    public string targetSlotId;
    public float spawnedAtSimulationTimeSeconds;

    public void Initialize(
        int sequenceNumber,
        string canonicalAccessPointId,
        string scenePairName,
        string canonicalAreaId,
        string sceneAreaId,
        string slotId,
        float simulationTimeSeconds)
    {
        spawnSequenceNumber = sequenceNumber;
        entranceAccessPointId = canonicalAccessPointId ?? string.Empty;
        entranceScenePairName = scenePairName ?? string.Empty;
        targetAreaId = canonicalAreaId ?? string.Empty;
        targetSceneAreaId = sceneAreaId ?? string.Empty;
        targetSlotId = slotId ?? string.Empty;
        spawnedAtSimulationTimeSeconds = Mathf.Max(0f, simulationTimeSeconds);
    }
}
