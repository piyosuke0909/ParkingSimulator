# NPC Phase 3 Road-Following Task List

作成日: 2026-06-12

## 目的

NPC車両が駐車後に出口へ向かう際、駐車枠・白線・隣接枠を横断せず、駐車場内の通路に沿って走る状態まで引き上げる。

現状は「出口へ向かう状態遷移」は入っているが、「道路面に収まる走行」はまだデータ不足で保証できない。

## 現状できていること

- [x] `Car_NPC.prefab` が存在する
- [x] Demo Car系の四角い車ではなく、NPC車モデルを使う下地がある
- [x] `NPCDriver` に `WaitingToExit`, `DrivingToExit`, `Exited` がある
- [x] 駐車完了後に `exitWaitSeconds` 待って出口ルートへ入る処理がある
- [x] `ParkingSlot.state` を `Reserved -> Occupied -> Empty` に戻す流れがある
- [x] `RoutePlanner` / `RoadGraph` / `RoadNode` でノード経路を作る下地がある
- [x] `ParkingSlotGeometry` で駐車枠内判定をする下地がある
- [x] `DrivableArea` で車体が走ってよい面を判定する下地がある
- [x] `VehicleCollisionShape` で車体サイズと前方BoxCastを扱う下地がある
- [x] 前向き駐車 / バック駐車の候補選択の下地がある

## 現状できていないこと

- [ ] 出口へ向かうルートが、駐車場通路だけを通る保証
- [ ] 白線や駐車枠を横断しない保証
- [ ] 通路の幅を使った車体フットプリント検証
- [ ] 曲がり角で車体が枠線・縁石・隣枠を踏まない検証
- [ ] 一方通行、進入禁止、右左折可否を使ったルート探索
- [ ] 駐車枠から通路へ戻る専用の出庫接続データ
- [ ] 複数NPC生成
- [ ] 複数NPCの予約管理を専用マネージャで行う処理
- [ ] 複数NPCの前方停止、譲り合い、再経路探索
- [ ] `SimulationTelemetry` による駐車時間・停止時間・再経路探索回数の記録
- [ ] 2/4/6/8/10台など同じseedで比較できる実験シナリオ
- [ ] Editor検証で、足りないノード・重複ID・不正なポリゴンを検出する仕組み

## 今の問題の直接原因

現在の `RoadNode` は、ほぼ「点」と「接続先」だけを持っている。

```text
RoadNode
├── nodeId
├── connectedNodes
├── traversalCost
├── speedLimit
└── stopAllowed
```

不足しているもの。

- `nodeType`
- `laneWidth`
- 道路エッジ専用データ
- 一方通行
- 右左折可否
- 道路中心線の細かい中間点
- 進入禁止エリア
- レーン境界
- 白線・駐車枠を避けるための禁止ポリゴン

そのため、`NPC_Demo_Mid -> NPC_Demo_Exit` のような長い接続があると、車はその2点を直線で結び、駐車枠や白線を横断する。

## 現プロジェクトから取得できるデータ

確認対象: `Assets/Scenes/Parking/SampleScene.unity`

### シーン構成

- Active Scene: `SampleScene`
- Scene Path: `Assets/Scenes/Parking/SampleScene.unity`
- Root Count: 4
- `ParkingAreas` 配下に `Area_A` から `Area_D` がある
- 各Areaに60枠あり、合計240枠相当の `ParkingSlot` がある
- `Waypoints` 配下にデモ用 `RoadNode` が4個ある
- `NPC_Phase1_RouteSystem` に `RoadGraph` と `RoutePlanner` がある
- `Car_NPC` がシーン上にある

### ParkingSlotから取れるデータ

各駐車枠から取れるもの。

- `slotId`
- `areaId`
- `state`
- `slotWidth`
- `slotDepth`
- `aisleWidth`
- `allowFrontIn`
- `allowReverseIn`
- `preferredManeuver`
- `slotBaseRenderer`
- 表示用色
- Transform位置
- Transform回転
- 子オブジェクトの白線メッシュ

代表値。

- 通常枠の `slotWidth`: 2.5
- 通常枠の `slotDepth`: 5
- 通常枠の `aisleWidth`: 6
- 通常枠の状態: `Empty`

不足または未接続のもの。

- 多くの枠で `parkingPoint` が未設定
- 多くの枠で `approachPoint` が未設定
- 多くの枠で `frontEntryPoint` が未設定
- 多くの枠で `reverseEntryPoint` が未設定
- 多くの枠で `roadNodeId` が空
- 多くの枠で `ParkingSlotGeometry` が未設定

### Slot_C_01から取れるデータ

`Slot_C_01` はデモ用に詳細データが入っている。

- `slotId`: `C-01`
- `areaId`: `C`
- 位置: `(18.25, 0, -86)`
- 回転: Y 90度
- `slotWidth`: 2.5
- `slotDepth`: 5
- `aisleWidth`: 6
- `allowFrontIn`: true
- `allowReverseIn`: true
- `ParkingSlotGeometry` あり
- `ManeuverPath` あり
  - `NPC_Demo_FrontInPath`
  - `NPC_Demo_ReverseInPath`
- 子Transformとして以下がある
  - `NPC_Demo_ParkingPoint`
  - `NPC_Demo_ApproachPoint`
  - `NPC_Demo_FrontEntryPoint`
  - `NPC_Demo_ReverseEntryPoint`

`Slot_C_01` の `ParkingSlotGeometry`。

- `corners`
  - `(12.5, 2, -83)`
  - `(12.5, 2, -89)`
  - `(24, 2, -89)`
  - `(24, 2, -83)`
- `innerPolygon`
  - `(12.75, 2, -83.25)`
  - `(12.75, 2, -88.75)`
  - `(23.75, 2, -88.75)`
  - `(23.75, 2, -83.25)`
- `allowedVehicleMargin`: 0.25

注意点。

- `Slot_C_01` には子Transformが存在するが、確認時点では `ParkingSlot.parkingPoint` / `approachPoint` / `frontEntryPoint` / `reverseEntryPoint` の参照フィールドが未設定に見える
- つまり、見た目の点は存在するが、コンポーネント参照として確実につながっているとは言い切れない
- デモ用セットアップ処理で実行時に補完している可能性があるため、保存済みシーンデータとしては不完全

### RoadGraph / RoadNodeから取れるデータ

`Waypoints` 配下のRoadNode。

| nodeId | position | connectedNodes |
| --- | --- | --- |
| `NPC_Demo_Entrance` | `(0, 2, -130)` | `NPC_Demo_Mid` |
| `NPC_Demo_Mid` | `(2.12499714, 2, -108)` | `NPC_Demo_SlotApproach`, `NPC_Demo_Exit` |
| `NPC_Demo_SlotApproach` | `(4.24999428, 2, -86)` | なし |
| `NPC_Demo_Exit` | `(130, 2, 130)` | なし |

`RoadGraph`。

- `nodesRoot`: `Waypoints`
- `nodes`: 上記4ノード
- `autoCollectOnAwake`: true
- `treatConnectionsAsBidirectional`: true

取れるもの。

- ノードID
- ノード座標
- 接続先
- ノード単位の `traversalCost`
- ノード単位の `speedLimit`
- ノード単位の `stopAllowed`

足りないもの。

- 実際の通路全体を表すノード数
- 曲がり角の中間ノード
- レーンID
- レーン幅
- 進行方向
- 一方通行
- edge単位のコスト
- edge単位の通行可否
- edge shape/polyline
- 右左折可否

現在の最大問題。

`NPC_Demo_Mid` から `NPC_Demo_Exit` が直接つながっているため、出口ルートが `(2.12499714, 2, -108) -> (130, 2, 130)` の長い斜め線になりやすい。この線が駐車枠や白線を横断する。

### DrivableAreaから取れるデータ

`NPC_Demo_DrivableArea` が1つある。

- `areaId`: `NPC_Demo_DrivableArea`
- `areaType`: `ParkingApproach`
- `defaultSpeedLimit`: 8
- `allowStop`: true
- `priority`: 100
- polygon:
  - `(-12, 0, -142)`
  - `(142, 0, -142)`
  - `(142, 0, 142)`
  - `(-12, 0, 142)`

取れるもの。

- 走行可能エリアの大きな矩形
- 車体四隅がその矩形内にあるか

足りないもの。

- 通路ごとの細かい走行可能ポリゴン
- 駐車枠を除外したポリゴン
- 白線を避けるバッファ
- 縁石、壁、柱などの進入禁止ポリゴン
- 出口専用エリア
- 入口専用エリア

現在の問題。

このポリゴンは広すぎるため、駐車枠の上を走っても「DrivableArea内」と判定される。

### Car_NPCから取れるデータ

`Car_NPC` から取れる主なデータ。

- Layer: `Car`
- `Car.carId`: `Car_NPC_001`
- `Car.length`: 10
- `Car.width`: 4.5
- `Rigidbody.mass`: 1200
- `Rigidbody.isKinematic`: true
- `PathFollower.moveSpeed`: 18
- `PathFollower.turnSpeed`: 8
- `NPCDriver.assignedSlot`: `Slot_C_01`
- `NPCDriver.exitNode`: `NPC_Demo_Exit`
- `NPCDriver.autoExitAfterParking`: true
- `NPCDriver.exitWaitSeconds`: 5
- `VehicleCollisionShape.bodyLength`: 10
- `VehicleCollisionShape.bodyWidth`: 4.5
- `VehicleCollisionShape.bodyHeight`: 3
- `VehicleCollisionShape.lookAheadDistance`: 8
- `CarController.maxForwardSpeed`: 18
- `CarController.maxReverseSpeed`: 8
- `CarController.wheelBase`: 2.5
- `CarController.maxSteerAngle`: 30

取れるもの。

- 車体サイズ
- 前方検知距離
- 速度設定
- 車両ID
- 現在状態
- 目標枠
- 出口ノード
- 見た目用 `Visual` 子

足りないもの。

- 実車スケールとして正しいホイール位置
- 実際の最小旋回半径
- 車両ごとの旋回可能な軌跡
- 車体外形の詳細メッシュ
- センサー用メッシュ
- タイヤ接地/ホイール物理
- ランダム色変更対象Materialの明確な指定

### DynamicObstacleから取れるデータ

`Car_NPC` に `DynamicObstacle` がある。

- `obstacleId`: `Car_NPC_001`
- `kind`: `Vehicle`
- `velocity`
- `collisionShape`
- `plannedRoute`

取れるもの。

- 他車扱いするための最低限のID
- 現在位置
- 現在回転
- 速度
- 車体当たり判定

足りないもの。

- 他車の予定ルート
- 優先度
- 車間距離
- 譲り合いルール
- 進路競合判定
- 通路占有時間

### ManeuverPathから取れるデータ

`Slot_C_01` には2つの駐車軌道がある。

`NPC_Demo_FrontInPath`

- `slotId`: `C-01`
- `maneuverType`: `FrontIn`
- controlPoints:
  - `NPC_Demo_FrontEntryPoint`
  - `NPC_Demo_ParkingPoint`
- `estimatedDuration`: 4
- `requiredClearance`: 0.2
- `maxSteeringAngle`: 35

`NPC_Demo_ReverseInPath`

- `slotId`: `C-01`
- `maneuverType`: `ReverseIn`
- controlPoints:
  - `NPC_Demo_ReverseEntryPoint`
  - `NPC_Demo_ParkingPoint`
- `estimatedDuration`: 5
- `requiredClearance`: 0.2
- `maxSteeringAngle`: 35

足りないもの。

- 駐車後に通路へ戻る出庫用ManeuverPath
- 切り返し軌道
- 曲線制御点
- 最小旋回半径に基づく検証
- 周囲車両がいる場合の代替軌道

## 必要なデータ

### 1. LaneNode

通路中心線を細かく表すノード。

- `nodeId`
- `position`
- `nodeType`
- `laneId`
- `laneWidth`
- `stopAllowed`
- `speedLimit`

必要な配置。

- 入口
- 通路の直線区間
- 曲がり角の手前
- 曲がり角の中間
- 曲がり角の出口
- 各駐車枠の前
- 出口導線

### 2. RoadEdge

ノード間の接続を表すデータ。

- `edgeId`
- `fromNodeId`
- `toNodeId`
- `oneWay`
- `speedLimit`
- `cost`
- `blocked`
- `allowedVehicleTypes`
- `turnType`
- `minTurningRadius`

今は `RoadNode.connectedNodes` だけなので、接続に意味を持たせられていない。

### 3. DrivableArea

車体が通ってよい面。

- 通路ごとのポリゴン
- 入口エリア
- 出口エリア
- 駐車枠前の余白エリア
- 曲がり角の通過可能エリア

今の広いデモ領域だと、駐車枠や白線の上も走行可能扱いになりやすい。

### 4. NoDriveArea

車体が入ってはいけない面。

- 駐車枠内
- 白線バッファ
- 縁石
- 壁
- 柱
- 歩行者通路
- 店舗前など車が入ってはいけない場所

`DrivableArea` だけで守る方法もあるが、デバッグと検証のためには禁止エリアを別で持った方が分かりやすい。

### 5. SlotRoadConnection

駐車枠と通路の接続。

- `slotId`
- `roadNodeId`
- `slotExitPoint`
- `slotEntryPoint`
- `frontEntryPoint`
- `reverseEntryPoint`
- `exitManeuverType`
- `requiredClearance`

駐車後にいきなり出口ノードへ向かうのではなく、まず「枠から通路へ出る」動作を明示する必要がある。

### 6. VehicleSpec

車両の物理的な大きさと旋回制約。

- `length`
- `width`
- `height`
- `wheelBase`
- `minTurningRadius`
- `frontOverhang`
- `rearOverhang`
- `sideSafetyMargin`
- `frontSafetyMargin`
- `rearSafetyMargin`

今は車体サイズの当たり判定下地はあるが、旋回半径やホイールベースを使った経路生成まではできていない。

## 他シミュレータ調査

### CARLA

CARLAはマップを3Dモデルだけでなく、OpenDRIVEベースの道路定義として扱う。Waypointはレーン上の向き付き3D点で、道路ID・レーンID・レーン幅・レーンマーキング・レーンチェンジ可否などを持つ。

参考:

- https://carla.readthedocs.io/en/latest/core_map/
- https://carla.readthedocs.io/en/latest/tuto_A_add_vehicle/

今回の不足に対応する点。

- `RoadNode` だけでなく、レーンIDとレーン幅が必要
- 白線や進入可否はレーンマーキング相当のデータとして持つべき
- 車両は実寸スケール、車体物理メッシュ、センサー用メッシュ、ホイール設定を分けている
- 今の `Car_NPC` は見た目・BoxColliderはあるが、本格的な車両物理、ホイール、旋回制約は未整備

### SUMO

SUMOは道路ネットワークをノード、エッジ、レーンで表す。レーンは速度、長さ、中心線shapeを持ち、交差点内の接続もinternal edgeとして扱う。

参考:

- https://sumo.dlr.de/docs/Networks/SUMO_Road_Networks.html
- https://sumo.dlr.de/docs/Definition_of_Vehicles,_Vehicle_Types,_and_Routes.html

今回の不足に対応する点。

- `RoadEdge` が必要
- レーン中心線は2点直線ではなく、複数点のpolylineとして持つべき
- 駐車場の曲がり角や合流部も、専用の接続エッジとして表すべき
- 車種ごとの長さ、速度、加速度などをVehicleTypeとして持つべき

### Unity AI Navigation

UnityのNavMeshAgentはNavMesh上を移動し、AgentのRadius、Height、AreaMask、Obstacle Avoidanceなどを使える。

参考:

- https://docs.unity3d.com/Packages/com.unity.ai.navigation@1.1/manual/NavigationOverview.html
- https://docs.unity3d.com/Packages/com.unity.ai.navigation@1.1/manual/NavMeshAgent.html

今回の不足に対応する点。

- 車体サイズ相当のRadius/範囲を考慮する必要がある
- AreaMaskのように「走ってよい場所/だめな場所」を分ける必要がある
- ただし駐車場の車は横移動できるキャラクターではないため、NavMesh丸投げより `RoadGraph + DrivableArea + VehicleSpec` の方が扱いやすい

## タスクリスト

### A. データ整備

- [ ] 駐車場通路の中心線を `LaneNode` として打つ
- [ ] 入口から出口まで、駐車枠を横断しないノード列を作る
- [ ] 各曲がり角に中間ノードを追加する
- [x] `RoadEdge` データを追加する
- [x] 一方通行・通行止め・速度制限を `RoadEdge` に持たせる
- [ ] 各駐車枠の `roadNodeId` を、最寄りの通路ノードに割り当てる
- [ ] `frontEntryPoint` / `reverseEntryPoint` / `slotExitPoint` を各枠に設定する
- [ ] 通路ごとの `DrivableArea` を作る
- [ ] 駐車枠・白線・縁石・壁を `NoDriveArea` として定義する
- [ ] 重複している `NPC_Demo_ApproachPoint` などのデモTransformを整理する

### B. 経路探索

- [x] `RoadGraph` を `RoadEdge` 対応にする
- [ ] 双方向固定ではなく、`oneWay` を見て探索する
- [ ] `blocked` なedgeを避ける
- [ ] 右左折可否を見て探索する
- [ ] `laneWidth` と `VehicleSpec.width` から通行可能か判定する
- [ ] 駐車枠から通路へ出る専用ルートを生成する
- [ ] 通路から出口へ向かうルートをedge列として生成する
- [ ] 生成されたルートが `DrivableArea` 内に収まるか検証する
- [ ] 生成されたルートが `NoDriveArea` と交差しないか検証する

### C. 車両挙動

- [ ] `PathFollower` の直線補間を、曲がり角で不自然になりにくい追従へ改善する
- [ ] `VehicleSpec.minTurningRadius` を使って急旋回を避ける
- [ ] 駐車完了後、まず枠から通路へ出る
- [ ] 通路に出てから出口ルートへ合流する
- [ ] 出口ルート上の各点で車体フットプリントを検査する
- [ ] 前方障害物があれば停止する
- [ ] 一定時間停止したら `Blocked` に入る
- [ ] `Blocked` から再経路探索または待機へ移る

### D. Phase 3本体

- [ ] `CarSpawner` を作る
- [ ] `SpawnFlow` を作る
- [ ] `DriverBehaviorProfile` を作る
- [ ] `ReservationManager` を作る
- [ ] 予約の有効期限を入れる
- [ ] 複数NPCで同じ枠を取り合わないようにする
- [ ] `LocalAvoidanceSettings` を作る
- [ ] 他車がいる通路に混雑コストを足す
- [ ] `SimulationScenario` を作る
- [ ] `SimulationTelemetry` を作る
- [ ] 同じseedで台数別比較ができるようにする

### E. 車アセット

- [ ] `Car_NPC` の実寸サイズを `VehicleSpec` と同期する
- [ ] `BoxCollider` と `VehicleCollisionShape` の寸法を同期する
- [ ] 車体色ランダム化の対象Materialを明確にする
- [ ] ホイール位置、ホイールベース、最小旋回半径を記録する
- [ ] 将来の正式車アセット差し替え用に `Visual` 子だけを交換できる構造にする
- [ ] 本格的な車両物理を使うか、現状のTransform制御を継続するか決める
- [ ] 本格物理を使う場合はWheelColliderまたは専用車両コントローラを導入する

### F. Editor検証

- [ ] `slotId` 重複を検出する
- [ ] `nodeId` 重複を検出する
- [ ] `roadNodeId` が存在しない枠を検出する
- [ ] `ParkingSlot` に `parkingPoint` / `approachPoint` がない場合に検出する
- [ ] `DrivableArea` の頂点不足を検出する
- [ ] `NoDriveArea` の頂点不足を検出する
- [ ] 入口から出口まで到達不能なグラフを検出する
- [ ] 経路が `NoDriveArea` を横断する場合に検出する
- [ ] 車体フットプリントが通路幅を超える箇所を検出する

## 優先順位

### 最優先

1. 駐車場通路の `LaneNode` を細かく打つ
2. `RoadEdge` を追加する
3. 通路だけの `DrivableArea` を作る
4. 駐車枠・白線を避ける `NoDriveArea` を作る
5. 出口ルートが禁止エリアを横断しないか検証する

### 次点

1. `PathFollower` を曲線・旋回半径対応にする
2. 駐車枠から通路へ出る専用動作を作る
3. 複数NPCの `ReservationManager` を作る

### 後回しでよい

1. 本格WheelCollider物理
2. センサー用メッシュ
3. 車両ライト制御
4. テレメトリの詳細分析
5. API/DB連携

## Phase 3判定

現在のPhase 3は、厳密には「Phase 3の一部だけ実装済み」。

実装済み。

- 単体NPCの駐車後待機
- 出口ルート開始
- 出口到達状態
- 駐車枠状態の解放

未実装。

- 複数台生成
- 複数台予約管理
- 交通需要
- 運転者プロファイル
- 混雑コスト
- 再経路探索
- テレメトリ
- 通路だけを走る保証

そのため、次の実装はPhase3本体を広げる前に、道路データを整備する方が優先。

## 実装開始手順

いきなり全240枠を対応せず、まず `Slot_C_01` だけで「駐車後に枠線を踏まず、通路に沿って出口へ向かう」状態を作る。

### Step 1: データ構造を追加する

- [x] `RoadEdge` を追加する
- [x] `RoadGraph` が `RoadEdge` を使って経路探索できるようにする
- [x] `RoadEdge` がない場合は既存の `RoadNode.connectedNodes` を使う
- [x] `RoadEdge` に `oneWay`, `blocked`, `speedLimit`, `cost`, `pathPoints` を持たせる

目的。

- ノード間を単純な直線ではなく、通路に沿った点列として扱う
- 将来、一方通行や通行止めをedge単位で扱えるようにする

### Step 2: Slot_C_01用の通路ノードを追加する

- [x] `Slot_C_01` 前の通路ノードを追加する
- [x] 通路の曲がり角ノードを追加する
- [x] 出口へ向かうノードを追加する
- [x] `NPC_Demo_Mid -> NPC_Demo_Exit` の長い斜め直線を使わないルートにする

最初に必要なノード。

| nodeId | 目的 |
| --- | --- |
| `Lane_C_01_Aisle` | `Slot_C_01` 前の通路 |
| `Lane_C_01_To_Main` | C列通路からメイン通路へ出る点 |
| `Lane_Main_Exit_01` | 出口方向へ進むメイン通路上の点 |
| `Lane_Main_Exit_02` | 出口手前の点 |
| `NPC_Demo_Exit` | 出口 |

### Step 3: Slot_C_01と道路を接続する

- [x] `Slot_C_01.roadNodeId` を `Lane_C_01_Aisle` にする
- [ ] `approachPoint` を `Lane_C_01_Aisle` 付近に接続する
- [ ] 駐車後にまず `slotExitPoint` 相当の点へ出る
- [ ] その後 `Lane_C_01_Aisle` へ合流する

現状は `Slot_C_01` に子Transformとして点はあるが、Inspector参照が未設定に見えるため、参照接続も確認対象にする。

### Step 4: 通路だけのDrivableAreaを追加する

- [x] `Slot_C_01` デモ用のC列前通路ポリゴンを作る
- [x] `Slot_C_01` デモ用のメイン通路ポリゴンを作る
- [x] `Slot_C_01` デモ用の出口ポリゴンを作る
- [x] 出口ルート検証は既存の広い `NPC_Demo_DrivableArea` に依存しない

目的。

- 駐車枠の上を走ってもOKにならないようにする
- 車体四隅が通路ポリゴン内に収まるか判定できるようにする

### Step 5: NoDriveAreaを追加する

- [x] `Slot_C_01` の駐車枠ポリゴンを進入禁止として扱う
- [ ] 白線まわりに安全バッファを持たせる
- [x] 最初は `Slot_C_01` 周辺だけでよい

目的。

- ルートが枠線や駐車枠を横断した時に検出できるようにする

### Step 6: ルート検証を追加する

- [x] 生成された出口経路を `DrivableArea` 内か確認する
- [x] 出口経路のサンプル点が `NoDriveArea` に入らないか確認する
- [x] 車体中心と四隅で通れるか確認する
- [x] NGなら警告を出して `Blocked` にする

補足: 現時点の検証対象は `Slot_C_01` から出口までのデモ経路。全駐車枠・全通路の検証エリア生成は未実装。

### Play確認結果

- [x] `Slot_C_01` に前駐車する
- [x] 駐車完了後に5秒待機する
- [x] 出口ルート検証で `DrivableArea` / `NoDriveArea` を通す
- [x] `Lane_C_01_Aisle -> Lane_C_Exit -> Main_Exit -> NPC_Demo_Exit` の通路ルートで出口へ向かう
- [x] `Car_NPC` が `Exited` まで到達する

確認時の出口ルート:

1. `(12.5, 2, -86)`
2. `(4.25, 2, -86)`
3. `(4.25, 2, -11.8)`
4. `(4.25, 2, 126)`
5. `(60.84, 2, 126)`
6. `(130, 2, 126)`
7. `(130, 2, 130)`

残り:

- [ ] 全駐車枠の `roadNodeId` と通路ポリゴンを生成する
- [ ] 白線・縁石・壁を含む `NoDriveArea` を全体に広げる
- [ ] AudioListener重複ログを整理する

### 追加しなければいけないデータ

現状の `RoadEdge` は「道に沿う点列」までは持てるが、「左側通行でどの位置を走るか」までは持っていない。出口も `Exit_Road_01` の中心をそのまま `NPC_Demo_Exit` にしているため、最後に駐車場外側へ進んでから向きを変えるように見える。

追加が必要なデータ。

- `laneCenterLine`: 車が実際に走るライン。道路中心ではなく、左側通行用にオフセットした点列。
- `laneSide`: `LeftHandTraffic` / `RightHandTraffic`。このプロジェクトではまず `LeftHandTraffic` 固定でよい。
- `laneWidth`: 車線幅。車体幅と安全マージンで通れるか判定する。
- `roadWidth`: 通路全体の幅。左右境界と車線中心を計算するために使う。
- `leftBoundary` / `rightBoundary`: 道路または車線の境界線。中央を走らない、白線を踏まない、縁石を踏まない判定に使う。
- `turnRadius`: 曲がり角で急に直角移動しないための最小旋回半径。
- `edge.pathPoints`: ノード間を直線で結ばず、通路に沿わせるための中間点。曲がり角では複数点を入れる。
- `edge.entryPoint` / `edge.exitPoint`: edgeに入る点と出る点。駐車枠から通路へ合流する位置を明確にする。
- `ExitApproachNode`: 駐車場内の出口手前ノード。まだ駐車場内道路として走る位置。
- `ExitGateNode`: 出口ゲートまたは境界線上のノード。ここを通過したら `Exited` 判定候補。
- `PublicRoadMergeNode`: 駐車場外道路に合流するノード。駐車場外まで走る表現が必要な場合だけ使う。
- `exitDirection`: 出口通過後に向くべき方向。最後に不自然な右左折をしないために使う。
- `NoDriveArea_Line`: 白線の進入禁止または踏み越え禁止ポリゴン。
- `NoDriveArea_Curb`: 縁石・壁・歩道など絶対に踏まない領域。
- `NoDriveArea_ParkingSlot`: 駐車中以外は横断しない駐車枠領域。

現時点の出口ルートで問題になっている箇所。

```text
(130, 2, 126) -> (130, 2, 130)
```

`(130, 130)` は `Exit_Road_01` の中心なので、出口手前で止まる/抜けるのではなく、外側の中心点まで進む。これを避けるには、出口を少なくとも `ExitApproachNode`, `ExitGateNode`, `PublicRoadMergeNode` に分ける。

### 実装優先順位

1. `Slot_C_01` の出口ルートを左側通行ラインにする
   - `RoadEdge.pathPoints` に左側通行用の点列を入れる。
   - 現在の道路中心寄りルートから、通路の左側へオフセットする。
   - まず1台・1枠だけで確認する。

2. 出口ノードを3段階に分ける
   - `ExitApproachNode`: 駐車場内の出口手前。
   - `ExitGateNode`: 出口境界。ここを通過したら `Exited` にしてよい。
   - `PublicRoadMergeNode`: 駐車場外道路へ合流する点。必要になるまで走行対象外でもよい。
   - これで「駐車場外まで走って右に曲がる」見え方を減らす。

3. `RoadEdge` に車線情報を追加する
   - `laneSide`
   - `laneWidth`
   - `roadWidth`
   - `turnRadius`
   - `centerLine` または `laneCenterLine`
   - 将来の複数NPCや対向車対応の下地にする。

4. 左側通行ラインの自動生成を作る
   - 手動で全edgeに点列を入れると破綻しやすい。
   - 道路中心線と幅から、左側走行ラインを生成できるようにする。
   - 最初は直線edgeだけ対応し、曲がり角は手動 `pathPoints` でよい。

5. 出口ルート検証を「通路内」から「車線内」へ強化する
   - 今は `DrivableArea` 内ならOK。
   - 次は車体四隅が `laneBoundary` 内に収まるか確認する。
   - 白線・縁石・駐車枠 `NoDriveArea` と交差しないかも確認する。

6. `Slot_C_01` 以外の代表枠へ広げる
   - A/B/C/Dから1枠ずつ選び、通路方向が違っても動くか確認する。
   - いきなり240枠に広げず、代表枠でルールを固める。

7. 全240枠へ `roadNodeId` と出庫接続を展開する
   - 各枠の最寄り通路ノードを割り当てる。
   - 前駐車/バック駐車後に通路へ戻る接続点を作る。

8. 複数NPC対応に進む
   - 通路予約、対向車、待機、譲り合い、再経路探索。
   - 左側通行ラインが入ってから進める方が確認しやすい。

### 最初の実装スコープ

今回の最初の実装は以下まで。

- [x] `RoadEdge` 追加
- [x] `RoadGraph` のedge対応
- [x] `Slot_C_01` から出口までの通路沿いedgeデータ追加
- [x] `Slot_C_01.roadNodeId` の設定
- [x] 出口ルートが `NPC_Demo_Mid -> NPC_Demo_Exit` の斜め直線を使わないこと

今回はまだやらない。

- [ ] 240枠すべての `roadNodeId` 設定
- [ ] 複数NPC生成
- [ ] 本格WheelCollider物理
- [ ] API/DB連携
