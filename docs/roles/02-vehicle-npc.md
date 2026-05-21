# 2人目：車・NPC担当

車の動作制御と複数台の NPC 管理を担当。シミュレーションの「動き」を作る役割。

---

## 担当範囲

- 車の物理挙動・移動制御
- 複数台の車を同時に動かす管理システム
- NPC（自動運転で動く車）の AI 制御
- 駐車・出庫の動作

---

## 管理するフォルダ

```
UnityProject/Assets/
├── Scripts/
│   ├── Vehicle/
│   │   ├── CarController.cs       ← 車の基本操作
│   │   ├── CarSpawner.cs          ← 複数台生成・管理
│   │   └── ParkingAction.cs       ← 駐車・出庫動作
│   └── NPC/
│       ├── NPCDriver.cs           ← NPC の自動運転
│       └── PathFollower.cs        ← 経路追従
├── Prefabs/
│   ├── Car_Player.prefab          ← 操作可能な車
│   └── Car_NPC.prefab             ← NPC 車
└── Models/
    └── Cars/                      ← 車の3Dモデル
```

---

## 他のメンバーとの連携ポイント

### 1人目（シーン）との連携
- 車のサイズ（幅・長さ）を共有して通路幅を合わせる
- `ParkingSpace` Tag が付いた Collider を駐車判定に使う
- 経路（NavMesh）が必要な場合は1人目のシーンに焼き込みが必要

### 3人目（データ・Web）との連携
- **駐車イベント**をデータとして渡す
  - どの車が・どのスペースに・何時に停めたか
  - `ParkingEvent` クラスを作って渡す形にする
- REST API への送信は3人目のコードを呼び出す形にする

### 4人目（統合）との連携
- Prefab の大きな変更前は事前に連絡
- `CarController.cs` のインターフェースが変わる場合は先に相談

---

## ブランチ運用

```powershell
git checkout -b feature/vehicle-npc
# 作業
git push origin feature/vehicle-npc
# → 4人目にPRをレビュー依頼
```

---

## 作業の進め方（推奨）

1. まず1台の車が動くところから始める（`CarController.cs`）
2. 駐車・出庫のアクション追加（`ParkingAction.cs`）
3. 複数台の管理（`CarSpawner.cs`）
4. NPC の自動運転（`NPCDriver.cs`）

---

## データ連携の仕様（3人目と要確認）

```csharp
// 駐車イベントのデータ構造（案）
public class ParkingEvent
{
    public string carId;
    public string spaceId;
    public string eventType;  // "park" or "leave"
    public DateTime timestamp;
    public Vector3 position;
}
```

---

## 注意事項

- NPC の経路計算は Unity の **NavMesh** を使うか、3人目の最適経路を使うか要検討
- 車の物理は `Rigidbody` を使う（`kinematic` にしない）
- 複数台を同時に動かすと重くなるので、画面外は無効化する仕組みを考える
