# NPC駐車判断データ設計

NPCが「どの枠に停めるか」「前向き駐車かバック駐車か」「どの経路で向かうか」を判断するためのデータ設計。

このドキュメントは実装前の共通仕様として使う。最初から高精度な車両物理を作るのではなく、MVPではUnity上のTransformと簡易スコアで動かし、あとから精度を上げる。

---

## 結論

実装できる。ただし、データは Phase 1 と Phase 2 に分けて考える。

現在のUnityプロジェクトには駐車枠の占有状態と検知点はあるが、NPCの判断に必要な「通路グラフ」「進入点」「駐車方法の可否」「車両サイズ」「予約状態」がまだ足りない。

Phase 1でまず追加すべきデータは以下の4種類。

| 種類 | 目的 | Unity側の主な置き場所 |
|------|------|----------------------|
| 駐車枠データ | どの枠にどう停められるか判断する | `ParkingSlot` |
| 通路グラフ | 入口から枠までの最短経路を出す | `Waypoints` / `RouteGraph` |
| 車両データ | 曲がれるか、枠に入るか判断する | `Car` / `VehicleSpec` |
| 動的状態 | 他車・混雑・予約を避ける | `ParkingLotManager` / API |

Phase 2では、白線内に収まるか、他車とぶつからないか、駐車軌道上に障害物がないかを判断するための安全・幾何データを追加する。

| 種類 | 目的 | Unity側の主な置き場所 |
|------|------|----------------------|
| 駐車枠ジオメトリ | 白線、枠境界、枠内判定を表す | `ParkingSlotGeometry` |
| 走行可能エリア | 車が走ってよい範囲を表す | `DrivableArea` |
| 車体当たり判定 | 車両サイズと安全マージンを表す | `VehicleCollisionShape` |
| 動的障害物 | 他車の位置、速度、予定経路を表す | `DynamicObstacle` |
| 駐車軌道 | 前向き/バック/切り返しの経路を表す | `ManeuverPath` |
| 交通ルール | 一方通行、停止線、優先通路を表す | `TrafficRule` |

ゲーム/交通シミュレーションの調査から、Phase 3以降では以下の運用データも導入する。

| 種類 | 目的 | Unity/API側の主な置き場所 |
|------|------|--------------------------|
| 車両物理プロファイル | 車重、重心、タイヤ、制動、操舵を表す | `VehiclePhysicsProfile` |
| 運転者プロファイル | 速度傾向、反応時間、安全距離、積極性を表す | `DriverBehaviorProfile` |
| 発生フロー | 何台が、いつ、どこから入るかを表す | `SpawnFlow` / `TrafficDemand` |
| 低速回避設定 | 前方検知、停止距離、優先度、譲り合いを表す | `LocalAvoidanceSettings` |
| テレメトリ | 駐車時間、停止時間、衝突、再経路探索を記録する | `SimulationTelemetry` |

---

## 現状

既存の `ParkingSlot` には次の情報がある。

```csharp
public string slotId;
public string areaId;
public ParkingSlotState state;
public Transform parkingPoint;
public Transform detectionPoint;
```

これでできること。

- 枠IDを持てる
- 空き、使用中、予約、無効を表現できる
- 駐車完了位置を持てる
- 車がいるか検知できる

まだできないこと。

- どの通路から入れるか判断する
- 前向き駐車とバック駐車を選ぶ
- 通路幅に対して車が曲がれるか判断する
- 他車が近くにいる時に待機、迂回する
- 複数台が同じ枠へ向かうのを防ぐ
- 白線や枠境界の内側に車体が収まるか判定する
- 駐車軌道上に他車や柱、壁がないか確認する
- 切り返しが必要か、何回まで許容するか判断する

---

## 追加するデータ

### ParkingSlot

既存の `ParkingSlot` を拡張して、駐車判断に必要な情報を持たせる。

| フィールド | 型の例 | 用途 |
|-----------|--------|------|
| `slotId` | `string` | 駐車枠ID |
| `areaId` | `string` | エリアID |
| `state` | `ParkingSlotState` | 空き、使用中、予約、無効 |
| `parkingPoint` | `Transform` | 駐車完了時の位置と向き |
| `detectionPoint` | `Transform` | 占有検知に使う点 |
| `approachPoint` | `Transform` | 通路上で駐車動作を始める点 |
| `frontEntryPoint` | `Transform` | 前向き駐車の進入開始点 |
| `reverseEntryPoint` | `Transform` | バック駐車の進入開始点 |
| `roadNodeId` | `string` | 接続する通路ノードID |
| `slotWidth` | `float` | 枠の幅 |
| `slotDepth` | `float` | 枠の奥行き |
| `aisleWidth` | `float` | 枠前の通路幅 |
| `allowFrontIn` | `bool` | 前向き駐車を許可するか |
| `allowReverseIn` | `bool` | バック駐車を許可するか |
| `preferredManeuver` | `ParkingManeuverPreference` | 優先する駐車方法 |

駐車方法のenum案。

```csharp
public enum ParkingManeuverPreference
{
    Auto,
    FrontInOnly,
    ReverseInOnly,
    PreferFrontIn,
    PreferReverseIn
}
```

MVPでは `approachPoint` と `parkingPoint` が特に重要。ここがあれば、NPCは「通路を走る」と「枠へ入る」を分けて実装できる。

---

### RoadGraph

駐車場内の道路をグラフとして持つ。

Unityの `Waypoints` 配下にノードを置き、接続情報を `RoadGraph` が管理する形が扱いやすい。

```text
Waypoints
├── Entrance_01
├── Lane_A_01
├── Lane_A_02
├── Lane_B_01
├── Exit_01
└── StoreEntrance_01
```

ノードに持たせるデータ。

| フィールド | 型の例 | 用途 |
|-----------|--------|------|
| `nodeId` | `string` | ノードID |
| `position` | `Vector3` | Unity座標 |
| `nodeType` | `RoadNodeType` | 入口、出口、通路、店舗入口など |
| `laneWidth` | `float` | 通路幅 |
| `stopAllowed` | `bool` | 一時停止できるか |
| `congestionWeight` | `float` | 混雑時の追加コスト |

エッジに持たせるデータ。

| フィールド | 型の例 | 用途 |
|-----------|--------|------|
| `fromNodeId` | `string` | 開始ノード |
| `toNodeId` | `string` | 終了ノード |
| `distance` | `float` | 距離 |
| `oneWay` | `bool` | 一方通行か |
| `speedLimit` | `float` | 走行速度の目安 |
| `blocked` | `bool` | 通行止めか |
| `cost` | `float` | 経路探索に使う重み |

最短経路はこのグラフに対してDijkstraまたはA*で計算する。

---

### VehicleSpec

車ごとのサイズや動きの制約を持つ。

```csharp
public class VehicleSpec
{
    public string vehicleType;
    public float length;
    public float width;
    public float minTurningRadius;
    public float forwardSpeed;
    public float reverseSpeed;
    public float parkingDuration;
}
```

MVPでは車種を1種類に固定してよい。あとで小型車、大型車、軽自動車などを増やす。

---

### VehicleState

NPCの現在状態を持つ。

| フィールド | 型の例 | 用途 |
|-----------|--------|------|
| `carId` | `string` | 車ID |
| `position` | `Vector3` | 現在位置 |
| `rotation` | `Quaternion` | 現在向き |
| `speed` | `float` | 現在速度 |
| `currentNodeId` | `string` | 近い通路ノード |
| `targetSlotId` | `string` | 目標枠 |
| `state` | `NPCDrivingState` | 入場、走行、駐車、出庫など |
| `assignedRoute` | `List<string>` | 通過ノード列 |
| `selectedManeuver` | `ParkingManeuverType` | 前向き/バック |

状態enum案。

```csharp
public enum NPCDrivingState
{
    Entering,
    SeekingSlot,
    DrivingToSlot,
    Parking,
    Parked,
    Leaving,
    DrivingToExit,
    Exited
}
```

---

### Reservation

複数台が同じ枠へ向かうのを防ぐため、空き枠を選んだ瞬間に予約する。

```csharp
public class ParkingReservation
{
    public string reservationId;
    public string carId;
    public string slotId;
    public DateTime reservedAt;
    public DateTime expiresAt;
}
```

MVPではUnity内だけで管理してよい。Web/APIを挟む段階では、API側でも同じ予約状態を持つ。

---

## Phase 2で追加する安全・幾何データ

Phase 1のデータがあれば、NPCは「空き枠へ向かって駐車する」ことはできる。ただし、それだけでは白線からはみ出す、駐車軌道上の他車にぶつかる、壁や柱を無視する可能性がある。

Phase 2では、駐車の見た目と安全性を上げるために以下のデータを追加する。

### ParkingSlotGeometry

駐車枠の白線や境界を、画像認識ではなく座標データとして持つ。

| フィールド | 型の例 | 用途 |
|-----------|--------|------|
| `slotId` | `string` | 対象の駐車枠ID |
| `corners` | `Vector3[]` | 駐車枠の四隅 |
| `innerPolygon` | `Vector3[]` | 白線内側の判定ポリゴン |
| `allowedVehicleMargin` | `float` | 車体が白線から離れるべき余白 |
| `frontBoundary` | `Vector3[]` | 枠の前方境界 |
| `leftBoundary` | `Vector3[]` | 左側の白線 |
| `rightBoundary` | `Vector3[]` | 右側の白線 |
| `rearBoundary` | `Vector3[]` | 奥側の境界 |

Unityシミュレーションでは、NPCに白線をカメラで見せる必要はない。白線に相当する正解データを持たせて、車体の四隅が `innerPolygon` に収まっているかを判定する。

MVPの判定は以下でよい。

```text
車体の四隅 + safetyMargin
→ ParkingSlotGeometry.innerPolygon の内側か確認
→ はみ出す場合は駐車失敗、または別のManeuverPathを試す
```

---

### DrivableArea

車が走ってよい範囲を表す。通路、入口、出口、駐車枠前の余白などをポリゴンで持つ。

| フィールド | 型の例 | 用途 |
|-----------|--------|------|
| `areaId` | `string` | 走行可能エリアID |
| `polygon` | `Vector3[]` | 走行可能範囲 |
| `areaType` | `DrivableAreaType` | 通路、入口、出口、駐車前スペースなど |
| `defaultSpeedLimit` | `float` | 速度制限 |
| `allowStop` | `bool` | 停止してよいか |
| `priority` | `int` | 優先度 |

`RoadGraph` は中心線の経路、`DrivableArea` は車体が通ってよい面として扱う。

```text
RoadGraph = どの順番で進むか
DrivableArea = その経路を車体が安全に通れるか
```

---

### VehicleCollisionShape

車体の当たり判定と安全マージンを持つ。

| フィールド | 型の例 | 用途 |
|-----------|--------|------|
| `vehicleType` | `string` | 車種 |
| `bodyLength` | `float` | 車体長 |
| `bodyWidth` | `float` | 車体幅 |
| `bodyHeight` | `float` | 車体高 |
| `frontOverhang` | `float` | 前方はみ出し量 |
| `rearOverhang` | `float` | 後方はみ出し量 |
| `sideSafetyMargin` | `float` | 左右の安全余白 |
| `frontSafetyMargin` | `float` | 前方の安全余白 |
| `rearSafetyMargin` | `float` | 後方の安全余白 |

Unityでは `BoxCollider` と `Physics.BoxCast` を使う。見た目モデルのColliderとは別に、NPC判断用の少し大きめの安全Colliderを持たせる。

---

### DynamicObstacle

他車や一時停止中の車を動的障害物として扱う。

| フィールド | 型の例 | 用途 |
|-----------|--------|------|
| `obstacleId` | `string` | 障害物ID |
| `kind` | `DynamicObstacleKind` | 他車、歩行者、停止車両など |
| `position` | `Vector3` | 現在位置 |
| `rotation` | `Quaternion` | 現在向き |
| `velocity` | `Vector3` | 現在速度 |
| `collisionShape` | `VehicleCollisionShape` | 当たり判定 |
| `plannedRoute` | `List<string>` | 予定経路 |
| `reservedTimeRange` | `TimeRange` | その経路を使う時間帯 |

MVPでは他車だけを対象にする。

```text
前方BoxCastで他車検知
→ 近ければ停止
→ 一定時間詰まったら再経路探索、または別枠へ変更
```

---

### ManeuverPath

駐車時の具体的な軌道を持つ。前向き、バック、切り返しを別々の候補として評価する。

| フィールド | 型の例 | 用途 |
|-----------|--------|------|
| `pathId` | `string` | 軌道ID |
| `slotId` | `string` | 対象の駐車枠 |
| `maneuverType` | `ParkingManeuverType` | 前向き、バック、切り返し |
| `controlPoints` | `Transform[]` | 軌道の制御点 |
| `requiresReverse` | `bool` | 後退が必要か |
| `estimatedDuration` | `float` | 所要時間 |
| `requiredClearance` | `float` | 必要な空き幅 |
| `maxSteeringAngle` | `float` | 想定ステア角 |
| `retryCount` | `int` | 切り返し回数 |

最初はBezierや物理ベースにしなくてよい。`ApproachPoint -> EntryPoint -> ParkingPoint` の点列を `ManeuverPath` として扱う。

---

### TrafficRule

駐車場内のルールを持つ。

| フィールド | 型の例 | 用途 |
|-----------|--------|------|
| `ruleId` | `string` | ルールID |
| `targetNodeId` | `string` | 対象ノード |
| `targetEdgeId` | `string` | 対象エッジ |
| `oneWay` | `bool` | 一方通行 |
| `stopRequired` | `bool` | 一時停止が必要か |
| `yieldRequired` | `bool` | 譲る必要があるか |
| `speedLimit` | `float` | 速度制限 |
| `noParking` | `bool` | 停車禁止か |

Phase 2では一方通行と一時停止だけでよい。優先通路や譲り合いはPhase 3以降でもよい。

---

## ゲーム/シミュレーション調査結果

車両NPCは、ゲームと交通シミュレーションで少し考え方が違う。駐車場MVPでは両方から必要な部分だけ採用する。

### Unity系ゲーム車両

Unityの `WheelCollider` は、車輪ごとに接地判定、ホイール物理、スリップベースのタイヤ摩擦、サスペンションを扱う。プロパティとして、ホイール半径、質量、ダンパー、サスペンション距離、スプリング、前後/横方向摩擦などを持つ。

ただし `WheelCollider` はレイキャスト型の車輪表現なので、段差や縁石では挙動確認が必要。駐車場MVPでは、最初から本格的な車両物理に寄せず、低速のTransform/簡易Rigidbody制御で成立させる。リアルなステアリングや揺れが必要になったら `WheelCollider` を導入する。

導入方針:

- Phase 1/2: 低速の簡易車両制御を使う
- Phase 3以降: 必要に応じて `VehiclePhysicsProfile` と `WheelPhysicsProfile` を追加する
- 段差、縁石、スロープを増やす場合は、地面Colliderを滑らかにして車輪挙動を検証する

---

### Unity NavMesh / ゲームAI

Unityの `NavMeshAgent` は、NavMesh上で経路探索と他エージェント回避を行う仕組み。速度、角速度、加速度、停止距離、半径、回避品質、優先度、Area Maskを持つ。

駐車場の車両では、そのまま `NavMeshAgent` に任せるより、道路中心線の `RoadGraph` と低速回避の `LocalAvoidanceSettings` を使う方が扱いやすい。理由は、車は横方向に自由移動するキャラクターではなく、通路、向き、最小回転半径、駐車軌道に強く制約されるため。

導入方針:

- `RoadGraph` を経路探索の主データにする
- `DrivableArea` を車体が走ってよい面として持つ
- `LocalAvoidanceSettings` で前方停止、優先度、再経路探索を管理する
- NavMeshは使うとしても、歩行者や広い空間の補助用途に限定する

---

### CARLA系自動運転シミュレーション

CARLAは車両を `VehiclePhysicsControl` と `WheelPhysicsControl` に分け、車重、抗力、重心、ステアリングカーブ、ギア、クラッチ、トルクカーブ、タイヤ摩擦、ブレーキトルクなどを持つ。さらにエージェントは、目的地、経路、信号、歩行者、前走車への反応を分けて扱う。

駐車場MVPでは、CARLAほど細かい物理は不要。ただし、将来的に比較実験の説得力を上げるなら、車両タイプごとの物理・制御パラメータを持つ価値がある。

導入方針:

- `VehicleSpec` は寸法だけでなく、制御上限も持つ
- `VehiclePhysicsProfile` はオプションとして追加する
- `DriverBehaviorProfile` で積極的なNPC、慎重なNPC、通常NPCを分ける
- 物理値は最初から厳密にしない。低速駐車に効く値から入れる

---

### SUMO系交通シミュレーション

SUMOは車両を「車種」「ルート」「車両インスタンス」に分ける。車種には加速度、減速度、車長、最大速度、安全距離に相当する `minGap` などを持たせ、車両には出発時刻、出発位置、出発速度、到着位置などを持たせる。交通量は `flow` として、一定間隔、確率、総数で発生させられる。

また、速度傾向は `speedFactor` の分布として扱い、交通シミュレーションでは全車が同じ速度で走らない前提になっている。横方向の動きも、車幅、横位置、横速度、レーン内の占有幅として管理される。

導入方針:

- 複数台比較には `SpawnFlow` / `TrafficDemand` を導入する
- `DriverBehaviorProfile` に `desiredSpeedFactor`, `reactionTime`, `minGap` を持たせる
- `SimulationScenario` に台数、発生間隔、滞在時間、乱数seedを持たせる
- 最適化あり/なしの比較は、同じseedと同じ発生フローで実行する

---

### このプロジェクトへの採用判断

採用する。

ただし、導入順は分ける。

| 優先度 | データ | 理由 |
|--------|--------|------|
| 高 | `DriverBehaviorProfile` | NPCの個体差、速度、車間、安全距離が必要 |
| 高 | `SpawnFlow` / `TrafficDemand` | 2/4/6/8/10台比較に必要 |
| 高 | `LocalAvoidanceSettings` | 他車との接触回避と停止判断に必要 |
| 高 | `SimulationTelemetry` | 効率化を数値で示すために必要 |
| 中 | `VehiclePhysicsProfile` | 車両挙動の説得力を上げるために必要 |
| 中 | `WheelPhysicsProfile` | WheelCollider導入時に必要 |
| 低 | `SensorProfile` | カメラ/疑似センサー表現を強める場合に必要 |

---

## 調査から追加するデータ

### VehiclePhysicsProfile

ゲーム車両や自動運転シミュレーションで使う、車体全体の物理設定。

| フィールド | 型の例 | 用途 |
|-----------|--------|------|
| `profileId` | `string` | 物理プロファイルID |
| `mass` | `float` | 車重 |
| `centerOfMass` | `Vector3` | 重心 |
| `dragCoefficient` | `float` | 空気抵抗の目安 |
| `maxSteerAngle` | `float` | 最大操舵角 |
| `steeringCurve` | `AnimationCurve` | 速度に応じた操舵制限 |
| `maxAcceleration` | `float` | 最大加速度 |
| `maxDeceleration` | `float` | 最大減速度 |
| `maxBrakeTorque` | `float` | 最大ブレーキ力 |
| `turningRadiusLowSpeed` | `float` | 低速時の最小回転半径 |

Phase 1/2では `maxSteerAngle`, `maxAcceleration`, `maxDeceleration`, `turningRadiusLowSpeed` だけでもよい。

---

### WheelPhysicsProfile

`WheelCollider` を使う段階で必要になる車輪ごとの設定。

| フィールド | 型の例 | 用途 |
|-----------|--------|------|
| `wheelRadius` | `float` | 車輪半径 |
| `wheelMass` | `float` | 車輪質量 |
| `wheelDampingRate` | `float` | 車輪回転の減衰 |
| `suspensionDistance` | `float` | サスペンション可動範囲 |
| `spring` | `float` | サスペンションばね |
| `damper` | `float` | サスペンション減衰 |
| `forwardFrictionStiffness` | `float` | 前後方向グリップ |
| `sidewaysFrictionStiffness` | `float` | 横方向グリップ |
| `brakeTorque` | `float` | ブレーキトルク |
| `handbrakeTorque` | `float` | ハンドブレーキトルク |

駐車場は低速なので、最初から細かいタイヤ摩擦を調整しない。車の見た目と制動距離に違和感が出た段階で導入する。

---

### DriverBehaviorProfile

NPCの運転傾向を持つ。全車が同じ動きをするとシミュレーションが不自然になるため、速度や安全距離に個体差を持たせる。

| フィールド | 型の例 | 用途 |
|-----------|--------|------|
| `profileId` | `string` | 運転者プロファイルID |
| `desiredSpeedFactor` | `float` | 制限速度に対する希望速度倍率 |
| `reactionTime` | `float` | 前方車両への反応遅れ |
| `minGap` | `float` | 停止時の最小車間距離 |
| `timeHeadway` | `float` | 走行時に保つ時間車間 |
| `aggressiveness` | `float` | 積極性 |
| `patience` | `float` | 待機許容時間 |
| `parkingPreference` | `ParkingManeuverPreference` | 駐車方法の好み |
| `maxReplanCount` | `int` | 再経路探索の上限 |

MVPでは `Normal`, `Cautious`, `Aggressive` の3種類でよい。

---

### SpawnFlow / TrafficDemand

NPCを何台、いつ、どこから出すかを定義する。

| フィールド | 型の例 | 用途 |
|-----------|--------|------|
| `flowId` | `string` | 発生フローID |
| `scenarioId` | `string` | シナリオID |
| `vehicleType` | `string` | 車種 |
| `count` | `int` | 発生台数 |
| `startTime` | `float` | 発生開始時刻 |
| `endTime` | `float` | 発生終了時刻 |
| `spawnInterval` | `float` | 発生間隔 |
| `spawnProbability` | `float` | 毎秒発生確率 |
| `entryNodeId` | `string` | 入場ノード |
| `exitNodeId` | `string` | 退場ノード |
| `dwellTimeRange` | `Vector2` | 駐車滞在時間の範囲 |
| `randomSeed` | `int` | 再現用seed |

2/4/6/8/10台の比較では、`count` と `randomSeed` を固定して最適化あり/なしを切り替える。

---

### LocalAvoidanceSettings

低速の接触回避と譲り合いを管理する。

| フィールド | 型の例 | 用途 |
|-----------|--------|------|
| `lookAheadDistance` | `float` | 前方検知距離 |
| `safetyRadius` | `float` | 車体周辺の安全半径 |
| `stopDistance` | `float` | 停止開始距離 |
| `yieldDistance` | `float` | 譲る判断距離 |
| `blockedTimeout` | `float` | 詰まり判定時間 |
| `priority` | `int` | 譲り合い優先度 |
| `obstacleLayerMask` | `LayerMask` | 検知対象レイヤー |
| `allowReverseRecovery` | `bool` | 詰まった時に後退復帰するか |

Phase 2では `lookAheadDistance`, `stopDistance`, `blockedTimeout`, `obstacleLayerMask` だけでよい。

---

### SimulationTelemetry

効率化の成果を測るための記録データ。

| フィールド | 型の例 | 用途 |
|-----------|--------|------|
| `scenarioId` | `string` | シナリオID |
| `runId` | `string` | 実行ID |
| `carId` | `string` | 車両ID |
| `routeId` | `string` | 経路ID |
| `targetSlotId` | `string` | 目標枠 |
| `enteredAt` | `float` | 入場時刻 |
| `parkedAt` | `float` | 駐車完了時刻 |
| `leftAt` | `float` | 出庫時刻 |
| `travelDistance` | `float` | 走行距離 |
| `waitingTime` | `float` | 停止/待機時間 |
| `replanCount` | `int` | 再経路探索回数 |
| `collisionCount` | `int` | 接触/衝突回数 |
| `parkingFailureCount` | `int` | 駐車失敗回数 |

Web管理画面と分析では、このデータを使って最適化あり/なしを比較する。

---

### SimulationScenario

比較実験の条件を固定する。

| フィールド | 型の例 | 用途 |
|-----------|--------|------|
| `scenarioId` | `string` | シナリオID |
| `totalVehicleCount` | `int` | 全体台数 |
| `optimizedVehicleCount` | `int` | 最適化対象台数 |
| `manualVehicleEnabled` | `bool` | 手動操作車を含めるか |
| `randomSeed` | `int` | 再現用seed |
| `durationSeconds` | `float` | 実行時間 |
| `routeMode` | `RouteMode` | 通常/最適化 |
| `spawnFlows` | `List<SpawnFlow>` | 発生フロー |

このデータがないと、同じ条件で比較できない。

---

## 前向き駐車・バック駐車の判断

NPCは各駐車方法にスコアを付け、低い方を選ぶ。

```text
score =
  routeDistance
  + maneuverTime
  + turningPenalty
  + congestionPenalty
  + collisionRisk
  + exitDifficulty
  + whiteLineBoundaryPenalty
  + maneuverPathClearancePenalty
```

### 前向き駐車を選びやすい条件

- 通路から枠へそのまま入りやすい
- 枠前の通路が広い
- 後続車を待たせたくない
- 出庫のしやすさより入庫時間を優先する

### バック駐車を選びやすい条件

- 出庫しやすさを重視する
- 通路幅が狭く、前向きで入ると切り返しが多い
- 枠の向きと車の進行方向が合わない
- デモ上、日本の駐車場らしい挙動を見せたい

### MVPの簡易スコア

最初はこれで十分。

```text
frontScore =
  routeDistance
  + frontParkingTime
  + anglePenalty
  + congestionPenalty

reverseScore =
  routeDistance
  + reverseParkingTime
  + anglePenalty
  + congestionPenalty
  - exitEaseBonus
```

ただし、`allowFrontIn == false` の場合は前向き駐車を候補から外す。`allowReverseIn == false` の場合も同じ。

Phase 2では、さらに安全スコアを足す。

```text
phase2SafetyScore =
  whiteLineBoundaryPenalty
  + obstacleCollisionRisk
  + drivableAreaViolationPenalty
  + requiredClearancePenalty
  + retryCountPenalty
```

候補の `ManeuverPath` が白線内に収まらない、走行可能エリアからはみ出す、他車のBoxCastに当たる場合は、その候補を選ばない。

---

## Unityでの実装方針

### 1. ParkingSlotを拡張する

既存の `ParkingSlot` に、駐車判断用のTransformと設定値を追加する。

```csharp
[Header("Maneuver Points")]
public Transform approachPoint;
public Transform frontEntryPoint;
public Transform reverseEntryPoint;

[Header("Maneuver Settings")]
public string roadNodeId;
public float slotWidth = 2.5f;
public float slotDepth = 5.0f;
public float aisleWidth = 6.0f;
public bool allowFrontIn = true;
public bool allowReverseIn = true;
public ParkingManeuverPreference preferredManeuver = ParkingManeuverPreference.Auto;
```

Prefab側では、各駐車枠の子オブジェクトとして以下を置く。

```text
ParkingSlot_A-01
├── ParkingPoint
├── DetectionPoint
├── ApproachPoint
├── FrontEntryPoint
└── ReverseEntryPoint
```

`ParkingPoint` の向きは「駐車完了時の車の向き」に合わせる。

---

### 2. RoadGraphを作る

追加するScript案。

| ファイル | 役割 |
|----------|------|
| `RoadNode.cs` | 通路上の一点を表す |
| `RoadEdge.cs` | ノード同士の接続を表す |
| `RoadGraph.cs` | ノードとエッジを集めて経路探索する |
| `RoutePlanner.cs` | 入口、枠、出口までのルートを返す |

Unity上では `Waypoints` 配下にノードを配置する。

```text
RouteGraph
Waypoints
├── Entrance_01
├── Lane_A_01
├── Lane_A_02
├── Lane_A_03
├── Lane_B_01
└── Exit_01
```

MVPでは、ノード接続はInspectorで手入力でもよい。後で自動生成にする。

---

### 3. NPCDriverを作る

`NPCDriver` は判断担当、`PathFollower` は移動担当に分ける。

```text
NPCDriver
├── 空き枠を探す
├── 枠を予約する
├── 入口からapproachPointまでのルートを取得する
├── 前向き/バック駐車のスコアを比較する
├── PathFollowerへ走行指示を出す
└── ParkingActionへ駐車指示を出す
```

`PathFollower` は受け取った座標列を順番に追従するだけにする。

```text
PathFollower
├── routePointsを受け取る
├── 次の点へ向かって進む
├── 到着したら次の点へ進む
└── 最後の点で完了通知を出す
```

---

### 4. ParkingActionを作る

駐車動作は走行経路と分ける。

前向き駐車の流れ。

```text
ApproachPoint
→ FrontEntryPoint
→ ParkingPoint
→ 停止
→ ParkingSlot.SetOccupied()
```

バック駐車の流れ。

```text
ApproachPoint
→ ReverseEntryPoint
→ 後退でParkingPoint
→ 停止
→ ParkingSlot.SetOccupied()
```

MVPではTransform補間でよい。リアルなステアリングや切り返しは後で追加する。

---

### 5. ReservationManagerを作る

空き枠を選んだ瞬間に `Reserved` にする。

```text
Empty
→ Reserved
→ Occupied
→ Empty
```

予約の有効期限を持たせると、NPCが途中で止まった時に枠が永久予約される問題を防げる。

---

### 6. Phase 2の安全データを作る

追加するScript案。

| ファイル | 役割 |
|----------|------|
| `ParkingSlotGeometry.cs` | 枠の四隅、白線内側ポリゴン、余白を持つ |
| `DrivableArea.cs` | 走行可能なポリゴン範囲を持つ |
| `VehicleCollisionShape.cs` | 車体サイズと安全マージンを持つ |
| `DynamicObstacleTracker.cs` | 他車の位置、速度、予定経路を集める |
| `ManeuverPath.cs` | 駐車軌道候補を持つ |
| `TrafficRule.cs` | 一方通行、停止線、速度制限を持つ |

Phase 2の最低ラインは以下。

```text
1. 車体のBoxColliderより少し大きい安全Boxを作る
2. 前方にBoxCastを飛ばして他車を検知する
3. 駐車軌道の各点で安全Boxが他車・壁・柱に当たらないか確認する
4. 駐車完了時の車体四隅が白線内側ポリゴンに収まるか確認する
5. 失敗したら別の駐車方法、別の枠、待機の順に試す
```

---

## API/DBに渡すデータ

WebAppやDBと連携する場合、Unityの座標と状態をAPIへ渡す。

### レイアウト情報

駐車場の静的情報。起動時または編集後に送る。

```json
{
  "parkingLotId": "demo_lot_001",
  "slots": [
    {
      "slotId": "A-01",
      "areaId": "A",
      "position": { "x": 10.0, "y": 0.0, "z": 20.0 },
      "rotationY": 90.0,
      "roadNodeId": "Lane_A_01",
      "slotWidth": 2.5,
      "slotDepth": 5.0,
      "aisleWidth": 6.0,
      "allowFrontIn": true,
      "allowReverseIn": true,
      "innerPolygon": [
        { "x": 8.75, "y": 0.0, "z": 17.5 },
        { "x": 11.25, "y": 0.0, "z": 17.5 },
        { "x": 11.25, "y": 0.0, "z": 22.5 },
        { "x": 8.75, "y": 0.0, "z": 22.5 }
      ],
      "safetyMargin": 0.2
    }
  ],
  "roadNodes": [
    {
      "nodeId": "Lane_A_01",
      "position": { "x": 8.0, "y": 0.0, "z": 20.0 },
      "nodeType": "Lane",
      "laneWidth": 6.0
    }
  ],
  "roadEdges": [
    {
      "fromNodeId": "Entrance_01",
      "toNodeId": "Lane_A_01",
      "distance": 20.0,
      "oneWay": true,
      "speedLimit": 5.0
    }
  ],
  "drivableAreas": [
    {
      "areaId": "Aisle_A",
      "areaType": "Lane",
      "polygon": [
        { "x": 0.0, "y": 0.0, "z": 15.0 },
        { "x": 80.0, "y": 0.0, "z": 15.0 },
        { "x": 80.0, "y": 0.0, "z": 25.0 },
        { "x": 0.0, "y": 0.0, "z": 25.0 }
      ]
    }
  ]
}
```

Phase 2でAPIへ渡す場合は、レイアウト情報に `innerPolygon`, `drivableAreas`, `trafficRules` を含める。WebApp側で正確な白線表示や混雑可視化をしたい場合にも使える。

### 車両状態

NPCの位置、状態、目標を定期送信する。

```json
{
  "carId": "Car_001",
  "position": { "x": 10.0, "y": 0.0, "z": 18.0 },
  "rotationY": 90.0,
  "speed": 3.2,
  "state": "DrivingToSlot",
  "targetSlotId": "A-01",
  "selectedManeuver": "ReverseIn"
}
```

### 駐車イベント

駐車、出庫、予約、キャンセルをイベントとして送る。

```json
{
  "carId": "Car_001",
  "slotId": "A-01",
  "eventType": "reserved",
  "timestamp": "2026-06-10T12:00:00+09:00",
  "position": { "x": 10.0, "y": 0.0, "z": 20.0 }
}
```

イベント種別案。

| eventType | 意味 |
|-----------|------|
| `reserved` | 枠を予約した |
| `reservation_cancelled` | 予約を解除した |
| `parked` | 駐車完了 |
| `left` | 出庫した |
| `entered` | 駐車場に入った |
| `exited` | 駐車場から出た |

---

## 実装順序

### Phase 1: 1台が固定ルートで駐車する

- `ParkingSlot` に `approachPoint` を追加
- `Waypoints` に入口から枠までの点を置く
- `PathFollower` で点列を追従する
- `ParkingAction` で `ParkingPoint` へ補間移動する
- 駐車方法は固定でよい

この段階では最適化しない。

---

### Phase 2: 白線・衝突回避・駐車軌道を追加する

- `ParkingSlotGeometry` で白線内側ポリゴンを持つ
- `DrivableArea` で走行可能エリアを持つ
- `VehicleCollisionShape` で安全マージン付きの車体判定を持つ
- `DynamicObstacle` で他車の位置と速度を扱う
- `ManeuverPath` で前向き/バック/切り返し軌道を候補化する
- `TrafficRule` で一方通行と一時停止を扱う
- `frontEntryPoint` と `reverseEntryPoint` を追加
- `allowFrontIn` / `allowReverseIn` を追加
- `preferredManeuver` を追加
- 安全スコア込みで前向き/バックを選ぶ

ここで初めて「白線内に収まるか」「他車とぶつからないか」「どの駐車軌道を選ぶか」まで判断できる。

---

### Phase 3: 複数台と予約

- `CarSpawner` でNPCを生成する
- `ReservationManager` で枠の取り合いを防ぐ
- 他車がいる通路に `congestionPenalty` を付ける
- `SpawnFlow` / `TrafficDemand` で入場タイミングを管理する
- `DriverBehaviorProfile` でNPCごとの速度、安全距離、待機傾向を変える
- `SimulationTelemetry` で駐車時間、停止時間、再経路探索回数を記録する
- 2台、4台、6台、8台、10台で比較できるようにする

---

### Phase 4: API/DB連携

- Unityからレイアウト情報をAPIへ送る
- Unityから車両状態とイベントをAPIへ送る
- WebAppはAPIから駐車状態、車両位置、ヒートマップを表示する
- 経路最適化をUnity内でやるかAPI側でやるかを切り替えられるようにする

---

### Phase 5: 精度を上げる

- 通路幅と最小回転半径で駐車可否を判定する
- 切り返し動作を入れる
- 他車の位置から衝突リスクを計算する
- 混雑している通路を避ける
- 警備員配置や誘導位置の最適化へつなげる

---

## 最初に作るべき最小セット

Phase 1の最初の実装では、以下だけでよい。

| 対象 | 必須データ |
|------|------------|
| `ParkingSlot` | `slotId`, `state`, `parkingPoint`, `approachPoint`, `allowFrontIn`, `allowReverseIn`, `preferredManeuver` |
| `RoadGraph` | `nodeId`, `position`, `connectedNodes`, `cost` |
| `Car` | `carId`, `length`, `width`, `currentState` |
| `NPCDriver` | `targetSlot`, `selectedManeuver`, `assignedRoute` |

この最小セットができれば、NPCは「入口から空き枠へ向かう」「前向き/バックを選ぶ」「駐車完了イベントを出す」まで実装できる。

Phase 2へ進む時の最小セットは以下。

| 対象 | 必須データ |
|------|------------|
| `ParkingSlotGeometry` | `slotId`, `innerPolygon`, `allowedVehicleMargin` |
| `DrivableArea` | `areaId`, `polygon`, `areaType` |
| `VehicleCollisionShape` | `bodyLength`, `bodyWidth`, `sideSafetyMargin`, `frontSafetyMargin`, `rearSafetyMargin` |
| `DynamicObstacle` | `obstacleId`, `position`, `rotation`, `velocity`, `collisionShape` |
| `ManeuverPath` | `pathId`, `slotId`, `maneuverType`, `controlPoints`, `estimatedDuration` |
| `TrafficRule` | `targetEdgeId`, `oneWay`, `stopRequired`, `speedLimit` |

この最小セットができれば、NPCは「白線内に収まる候補だけ選ぶ」「前方車両がいれば止まる」「駐車軌道上に障害物があれば別候補を試す」まで実装できる。

Phase 3へ進む時の最小セットは以下。

| 対象 | 必須データ |
|------|------------|
| `DriverBehaviorProfile` | `desiredSpeedFactor`, `reactionTime`, `minGap`, `patience`, `parkingPreference` |
| `SpawnFlow` | `count`, `spawnInterval`, `entryNodeId`, `exitNodeId`, `dwellTimeRange`, `randomSeed` |
| `LocalAvoidanceSettings` | `lookAheadDistance`, `stopDistance`, `blockedTimeout`, `obstacleLayerMask` |
| `SimulationTelemetry` | `runId`, `carId`, `enteredAt`, `parkedAt`, `travelDistance`, `waitingTime`, `replanCount` |
| `SimulationScenario` | `totalVehicleCount`, `optimizedVehicleCount`, `manualVehicleEnabled`, `routeMode`, `randomSeed` |

この最小セットができれば、NPCを複数台発生させ、同じ条件で最適化あり/なしを比較できる。

---

## MVPチェックリスト

### Phase 1完了条件

- [ ] `ParkingSlot` に `slotId`, `state`, `parkingPoint`, `approachPoint` がある
- [ ] `Waypoints` / `RoadGraph` が入口から少なくとも1つの駐車枠までつながっている
- [ ] 1台のNPCが入口から `approachPoint` まで移動できる
- [ ] `ParkingAction` で `parkingPoint` へ停車できる
- [ ] 駐車完了時に `ParkingSlot.state` が `Occupied` になる
- [ ] 出庫時に `ParkingSlot.state` が `Empty` に戻る
- [ ] 経路、目標枠、現在状態をデバッグ表示できる

### Phase 2完了条件

- [ ] `ParkingSlotGeometry.innerPolygon` が各駐車枠に設定されている
- [ ] `DrivableArea` が通路と駐車前スペースに設定されている
- [ ] 壁、柱、縁石、他車のColliderレイヤーが分かれている
- [ ] `VehicleCollisionShape` が車体サイズと安全マージンを持つ
- [ ] 前方 `BoxCast` で他車を検知して停止できる
- [ ] `ManeuverPath` が前向き/バック駐車の候補を持つ
- [ ] 駐車完了時に車体四隅が白線内側に収まるか判定できる
- [ ] 駐車軌道上に障害物があれば別候補、別枠、待機の順に切り替えられる
- [ ] 白線ポリゴン、走行可能エリア、BoxCast範囲をデバッグ表示できる

### Phase 3完了条件

- [ ] `SpawnFlow` で2/4/6/8/10台を同じ条件で発生できる
- [ ] `DriverBehaviorProfile` で通常、慎重、積極的なNPCを分けられる
- [ ] `ReservationManager` が枠の重複予約を防ぐ
- [ ] 予約に有効期限があり、失敗時に解除できる
- [ ] 前方停止が一定時間続いた時に `Blocked` へ遷移する
- [ ] `Blocked` から再経路探索または別枠選択に移れる
- [ ] `SimulationTelemetry` で駐車時間、走行距離、停止時間、再経路探索回数を取れる
- [ ] 最適化なし/ありで同じseedを使って比較できる

### API/DB連携チェック

- [ ] `layout` として駐車枠、道路グラフ、白線ポリゴン、走行可能エリアを送れる
- [ ] `vehicle_state` として車両位置、速度、状態、目標枠、経路を送れる
- [ ] `parking_event` として入場、予約、駐車開始、駐車完了、出庫、退出を送れる
- [ ] `telemetry` として実行ごとの評価指標を保存できる
- [ ] WebAppが枠状態、車両位置、経路、混雑、ヒートマップを表示できる

### Editor検証チェック

- [ ] `slotId` が重複していない
- [ ] `parkingPoint` / `approachPoint` が未設定の枠を検出できる
- [ ] `roadNodeId` が存在しない枠を検出できる
- [ ] `RoadGraph` が入口から出口まで接続している
- [ ] `innerPolygon` の頂点数不足や反転を検出できる
- [ ] `ManeuverPath` の制御点不足を検出できる
- [ ] 壁、柱、車、駐車枠TriggerのLayer設定ミスを検出できる

---

## 担当ごとの作業分担

### 1人目: 駐車場シーン

- 各駐車枠に `ParkingPoint` を置く
- 各駐車枠に `ApproachPoint` を置く
- 通路上に `Waypoints` を置く
- 通路幅や一方通行のルールを決める
- Phase 2では白線内側ポリゴン、走行可能エリア、壁・柱Colliderを整備する

### 2人目: 車・NPC

- `PathFollower` を作る
- `NPCDriver` を作る
- `ParkingAction` を作る
- 前向き/バック駐車のスコア計算を作る
- Phase 2では `BoxCast` による前方検知と `ManeuverPath` の安全判定を作る

### 3人目: データ・Web

- レイアウト情報のAPIモデルを作る
- 車両状態のAPIモデルを作る
- 予約、駐車、出庫イベントを受け取る
- Web上で枠状態と車両位置を表示する
- Phase 2では白線ポリゴン、走行可能エリア、他車位置を可視化できるようにする

### 4人目: 統合

- Unity、API、Webのデータ名を揃える
- `slotId`, `carId`, `nodeId` の命名ルールを統一する
- MVPの比較条件を管理する
- `SimulationScenario` と `randomSeed` を固定し、比較実験の再現性を管理する

---

## 注意点

- 最初からリアルな車両物理を作り込まない。
- 駐車判断は、まずTransformベースの簡易実装で成立させる。
- 白線は画像認識ではなく、`ParkingSlotGeometry.innerPolygon` として座標データで扱う。
- 他車衝突は最初から完全な予測制御にせず、前方 `BoxCast` と停止で始める。
- ゲーム車両物理は最初から `WheelCollider` に寄せすぎない。低速駐車が成立してから導入判断する。
- 交通シミュレーションは、車種、ルート、発生フロー、運転者プロファイルを分けて扱う。
- 比較実験は必ず同じ `SimulationScenario` と `randomSeed` で実行する。
- `ParkingSlot.state` は検知結果だけでなく、予約状態にも使う。
- WebAppと連携する場合、Unity座標系をそのまま渡すとPWAのマップ表示が作りやすい。
- API/DB側で経路最適化する場合でも、Unity側には最低限の `RoadGraph` と `ParkingSlot` 接続情報が必要。

---

## 参考にした一次情報

- Unity Manual: Wheel collider component reference
  - https://docs.unity3d.com/Manual/class-WheelCollider.html
- Unity Manual: Introduction to Wheel colliders
  - https://docs.unity3d.com/Manual/wheel-colliders-introduction.html
- Unity AI Navigation: NavMesh Agent component reference
  - https://docs.unity3d.com/Packages/com.unity.ai.navigation@1.1/manual/NavMeshAgent.html
- Unity AI Navigation: Navigation Areas and Costs
  - https://docs.unity3d.com/Packages/com.unity.ai.navigation@1.1/manual/AreasAndCosts.html
- CARLA Python API: VehiclePhysicsControl / WheelPhysicsControl
  - https://carla.readthedocs.io/en/latest/python_api/
- CARLA Agents
  - https://carla.readthedocs.io/en/latest/adv_agents/
- SUMO: Definition of Vehicles, Vehicle Types, and Routes
  - https://sumo.dlr.de/docs/Definition_of_Vehicles,_Vehicle_Types,_and_Routes.html
- SUMO: SublaneModel
  - https://sumo.dlr.de/docs/Simulation/SublaneModel.html
