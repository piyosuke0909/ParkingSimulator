# Car_NPC 実装計画

`/Users/kyhkt/Downloads/NPC.zip` を参考にしつつ、不足している依存を Codex 側で補完して、既存の Phase 2 NPC 駐車制御に接続するための計画。

## 目的

`DummyCar_001` を、今後差し替え可能な `Car_NPC` prefab に置き換える。

最初の到達点は以下。

- Unity を実行すると `Car_NPC` が入口から走行する
- 空き駐車枠を選ぶ
- 前向き駐車またはバック駐車を選択する
- Phase 2 の白線内判定と障害物判定を通す
- 駐車完了後に `ParkingSlot.state` が `Occupied` になる

## 現状分析

`NPC.zip` に含まれている主なファイル。

| ファイル | 状態 |
| --- | --- |
| `NPCCar.prefab` | 外部 prefab / model GUID に依存しており、単体では完全に復元できない |
| `NPCAutomatedParkingController.cs` | 独自の駐車AIを持つ。既存 `NPCDriver` と責務が重なる |
| `NPCColliderVisualizer.cs` | Collider可視化用。利用可能 |
| `BoxCollider.preset` | Collider設定の参考として利用可能 |
| `Rigidbody.preset` | Rigidbody設定の参考として利用可能 |
| `CarController.preset` | `CarController` が不足しているため、そのままでは使えない |

不足している依存。

| 依存 | 対応方針 |
| --- | --- |
| `guid: 21c7e49b14231c348ab698b9022b8cb7` | zip外のベースprefab。現状は復元不可。使わない |
| `guid: 28e6efeab80060a44aeb418ac1f92bce` | zip外の車モデルまたは子prefab。現状は復元不可。使わない |
| `CarController` | Codex側で最小互換クラスを追加する |
| `CarObstacleDetection` | 既存 `VehicleCollisionShape` に寄せた互換クラスを追加する |
| `CarLightingSystem` | 最小のライト制御互換クラスを追加する |

## 基本方針

`NPCCar.prefab` をそのまま取り込まず、プロジェクト内で新しい `Car_NPC.prefab` を組み直す。

理由は以下。

- 元prefabとモデルがzipに含まれていない
- `NPCAutomatedParkingController` は既存の `NPCDriver` / `ParkingAction` と制御が競合する
- 現在のPhase 2実装は `Transform` 補間、白線内判定、BoxCast検知を前提にしている

したがって、zipは「車体サイズ、Collider、Rigidbody、補助スクリプトの参考」として使う。

## 予定フォルダ構成

```text
UnityProject/Assets/Prefabs/Cars/Car_NPC/
├── implementation-plan.md
├── Car_NPC.prefab
└── Raw/
    └── NPC.zip 展開元の参考ファイル

UnityProject/Assets/Scripts/Vehicle/CarNPC/
├── CarController.cs
├── CarObstacleDetection.cs
└── CarLightingSystem.cs
```

`Raw/` は必要になった場合のみ作る。まずは計画書だけ置く。

## Car_NPC prefab 構成

最終的な `Car_NPC.prefab` は以下の構成にする。

```text
Car_NPC
├── Visual
├── Collider
└── Components
    Car
    NPCDriver
    PathFollower
    ParkingAction
    VehicleCollisionShape
    DynamicObstacle
    Rigidbody
    BoxCollider
```

初期段階では `Visual` は簡易メッシュでよい。あとで正式な車アセットが来たら `Visual` だけ差し替える。

## 追加する互換スクリプト

### CarController

目的は、外部zip由来のコードが参照してもコンパイルできるようにすること。

最初は既存 `PathFollower` と競合させないため、直接運転制御には使わない。

最低限の責務。

- `Throttle`, `Steering`, `Brake` 相当の入力値を保持する
- `Rigidbody` があれば速度情報を返す
- 既存 `NPCDriver` が動作する場合は自走処理をしない

### CarObstacleDetection

既存 `VehicleCollisionShape` を内部で使う互換レイヤーにする。

最低限の責務。

- 前方障害物があるかを返す
- `VehicleCollisionShape.TryGetForwardObstacle()` に委譲する
- 外部AIを使わない場合は補助用途に留める

### CarLightingSystem

見た目補助として最小実装にする。

最低限の責務。

- ブレーキライトON/OFF
- 左右ウインカーON/OFF
- 対応するLightやRendererが未設定でも落ちない

## 既存Phase 2との接続

メイン制御は既存実装を使う。

| 役割 | 使用コンポーネント |
| --- | --- |
| 枠選択 | `NPCDriver` |
| 前向き/バック選択 | `NPCDriver.selectedManeuver` |
| 経路走行 | `PathFollower` |
| 駐車動作 | `ParkingAction` |
| 白線内判定 | `ParkingSlotGeometry` |
| 障害物検知 | `VehicleCollisionShape` |
| 動的障害物表現 | `DynamicObstacle` |

`NPCAutomatedParkingController` は当面使わない。取り込む場合も無効化して参考コード扱いにする。

## 実装手順

1. `NPC.zip` を一時展開して依存を再確認する
2. `CarController`, `CarObstacleDetection`, `CarLightingSystem` の最小互換クラスを追加する
3. 新規 `Car_NPC.prefab` を作る
4. `Car_NPC.prefab` に既存Phase 2コンポーネントを付与する
5. `VehicleCollisionShape` のサイズをzip内 `BoxCollider` 設定に合わせる
6. `NpcPhase1DemoSetup` が `Car_NPC` を優先して探すようにする
7. `DummyCar_001` がない場合でも `Car_NPC` でデモが動くようにする
8. Unityで `Idle -> SeekingSlot -> DrivingToSlot -> Parking -> Parked` を確認する

## 実行前チェック

- `NPCCar.prefab` をそのままシーンに置かない
- `NPCAutomatedParkingController` と `NPCDriver` を同時に有効化しない
- `VehicleCollisionShape.obstacleLayerMask` は最初は未設定または必要レイヤーのみ
- `BoxCollider` は車体サイズに合わせる
- Rigidbodyを使う場合は、既存のTransform補間と干渉しない設定にする

## 完了条件

- `Car_NPC.prefab` が `Assets/Prefabs/Cars/Car_NPC/` に存在する
- `DummyCar_001` なしでもデモセットアップが動く
- Unity ConsoleにMissing Scriptが出ない
- Unity ConsoleにMissing Prefab参照が出ない
- NPCが入口から駐車完了まで進む
- `ParkingSlot.state` が `Reserved -> Occupied` に遷移する

## 保留事項

- 正式な車モデルが来たら `Visual` を差し替える
- 複数台生成は Phase 3 の `CarSpawner` で対応する
- 出庫、退出、再入場は車ライフサイクル実装時に追加する
- 音関連の `AudioListener` 警告は現時点では無視する
