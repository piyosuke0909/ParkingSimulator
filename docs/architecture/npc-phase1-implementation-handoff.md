# NPC Phase 1 実装ハンドオフ

チームメンバーへ渡すための実装指示書。  
まずは「1台のNPCが入口から空き駐車枠へ向かい、停車して、枠状態がOccupiedになる」ところまでを作る。

詳細なデータ設計は [NPC駐車判断データ設計](npc-parking-decision-data.md) を参照する。

---

## 最初のゴール

Phase 1では、リアルな自動運転や複数台制御は作らない。  
最初のPRでは以下だけを完成させる。

```text
DummyCar_001
→ 入口付近から出発
→ RoadGraphの経路をたどる
→ 1つの空きParkingSlotを予約する
→ ApproachPointへ移動する
→ ParkingPointへ入庫する
→ ParkingSlot.stateがOccupiedになる
```

この縦切りが動けば、後で複数台、白線判定、衝突回避、API連携を足せる。

---

## 現在の前提

- Unityバージョン: `2022.3.62f2`
- メインの駐車場シーン: `UnityProject/Assets/Scenes/Parking/SampleScene.unity`
- 駐車枠は約240枠ある
- `DummyCar_001` から `DummyCar_004` の4台がシーンにある
- `Waypoints` オブジェクトはあるが、中身はまだ空
- 既存の `Car.cs` は `carId` を持つだけ
- 既存の `ParkingSlot.cs` は `slotId`, `areaId`, `state`, `parkingPoint`, `detectionPoint` を持つ

---

## 結局必要なデータ

必要なデータは、段階で分けて考える。

### Phase 1: とりあえず1台が駐車するために必要

駐車枠データ:

- `slotId`
- 空き/使用中/予約状態
- 駐車完了位置 `parkingPoint`
- 駐車開始位置 `approachPoint`

道路データ:

- 入口ノード
- 通路ノード
- ノード同士の接続
- 駐車枠につながるノード

車データ:

- `carId`
- 現在位置
- 現在向き
- 移動速度

NPC状態データ:

- 今の状態: 入場中、走行中、駐車中など
- 目標の駐車枠
- たどる経路

予約データ:

- どの車がどの枠を予約しているか
- 予約の有効期限

ここまでで「1台が空いている枠に向かって停まる」はできる。

---

### Phase 2: 白線内に停める・ぶつからないために必要

駐車枠の形データ:

- 駐車枠の四隅
- 白線内側の範囲
- 車体がはみ出してよい余白

走行可能エリア:

- 車が走っていい通路範囲
- 進入禁止エリア
- 壁、柱、縁石の位置

車体サイズ/当たり判定:

- 車の長さ
- 車の幅
- 安全マージン

他車データ:

- 他車の位置
- 他車の向き
- 他車の速度
- 他車の予定経路

駐車軌道データ:

- 前向き駐車の軌道
- バック駐車の軌道
- 切り返しポイント

交通ルール:

- 一方通行
- 停止位置
- 優先通路
- 速度制限

ここまでで「白線内に収める」「他車や壁にぶつからない」「前向き/バックを選ぶ」ができる。

---

### Phase 3: 複数台でシミュレーションするために必要

車両発生データ:

- 何台出すか
- 何秒ごとに入場するか
- どの入口から入るか
- どの出口から出るか
- 何秒駐車するか

運転者タイプ:

- 慎重
- 普通
- 積極的
- 車間距離
- 反応時間
- 希望速度

回避設定:

- 前方を見る距離
- 停止する距離
- 詰まったと判断する時間
- 譲る優先度

シナリオ条件:

- 全体台数
- 最適化対象の台数
- ランダムseed
- 実行時間
- 最適化あり/なし

ここまでで「2/4/6/8/10台を同じ条件で比較する」ができる。

---

### Phase 4: Web/API/DBに必要

レイアウトデータ:

- 駐車枠一覧
- 道路ノード
- 白線範囲
- 走行可能エリア

車両状態データ:

- 車の位置
- 速度
- 状態
- 目標枠
- 経路

イベントデータ:

- 入場
- 予約
- 駐車開始
- 駐車完了
- 出庫
- 退出
- 詰まり
- 再経路探索
- 衝突検知

分析データ:

- 駐車完了までの時間
- 走行距離
- 待機時間
- 再経路探索回数
- 衝突回数
- 駐車失敗回数
- エリアごとの混雑度

---

### 最初に絶対必要なもの

Phase 1で最初に必要なのは以下。

1. 駐車枠の位置と状態
2. 駐車開始位置 `approachPoint`
3. 駐車完了位置 `parkingPoint`
4. 道路ノードと接続情報
5. 車の現在位置と目標枠
6. 枠の予約状態

次に必要なのは以下。

1. 白線内側の範囲
2. 走行可能エリア
3. 車体サイズと安全マージン
4. 他車の位置
5. 駐車軌道

ここまであれば、MVPとして説得力のあるNPC駐車シミュレーションになる。

---

## 今回実装したもの

### 1. ParkingSlotの拡張

対象:

```text
UnityProject/Assets/Scripts/Parking/ParkingSlot.cs
```

追加するデータ:

| フィールド | 用途 |
|-----------|------|
| `approachPoint` | 通路上で駐車動作を始める位置 |
| `frontEntryPoint` | 前向き駐車の進入点 |
| `reverseEntryPoint` | バック駐車の進入点 |
| `roadNodeId` | この駐車枠に接続する道路ノード |
| `allowFrontIn` | 前向き駐車を許可するか |
| `allowReverseIn` | バック駐車を許可するか |
| `preferredManeuver` | 優先する駐車方法 |

Phase 1では `approachPoint` と `parkingPoint` があれば最低限動く。  
`frontEntryPoint` と `reverseEntryPoint` はPhase 2用に先に持たせる。

---

### 2. 道路グラフ

実装済み:

```text
UnityProject/Assets/Scripts/Navigation/RoadNode.cs
UnityProject/Assets/Scripts/Navigation/RoadGraph.cs
UnityProject/Assets/Scripts/Navigation/RoutePlanner.cs
```

役割:

| ファイル | 役割 |
|----------|------|
| `RoadNode.cs` | 道路上の地点。`nodeId` と接続先を持つ |
| `RoadGraph.cs` | シーン内のRoadNodeを集める |
| `RoutePlanner.cs` | 入口から目的地までの経路を返す |

Phase 1では最短経路アルゴリズムを作り込みすぎない。  
まずは手置きしたノードを順番にたどれるだけでよい。

---

### 3. 車の移動

実装済み:

```text
UnityProject/Assets/Scripts/Vehicle/PathFollower.cs
```

役割:

- `List<Transform>` または `List<Vector3>` の経路を受け取る
- 次の点へ向かって移動する
- 最後の点に着いたら完了イベントを出す

Phase 1ではTransform移動でよい。  
`WheelCollider` や本格的な車両物理はまだ使わない。

---

### 4. NPC判断

実装済み:

```text
UnityProject/Assets/Scripts/Vehicle/NPCDriver.cs
```

役割:

- 空き枠を1つ選ぶ
- 選んだ枠を `Reserved` にする
- `RoutePlanner` から `approachPoint` までの経路を受け取る
- `PathFollower` へ経路を渡す
- 到着後に `ParkingAction` を呼ぶ

Phase 1では「一番近い空き枠」または「Inspectorで指定した枠」でよい。

---

### 5. 駐車動作

新規作成候補:

```text
UnityProject/Assets/Scripts/Vehicle/ParkingAction.cs
```

役割:

- `approachPoint` から `parkingPoint` へゆっくり移動する
- 停車後に `ParkingSlot.SetOccupied()` を呼ぶ

Phase 1では前向き/バックの選択は固定でよい。  
車体の四隅、白線、他車衝突はPhase 2で扱う。

---

## シーン側で必要な作業

1人目または統合担当が行う。

- `Waypoints` 配下にテスト用ノードを3〜6個置く
- 入口付近からテスト対象の駐車枠までつなぐ
- テスト対象の駐車枠に `ApproachPoint` を置く
- `parkingPoint` の向きを駐車完了時の車の向きに合わせる
- 最初は1〜3枠だけ設定すればよい

全240枠を最初から対応しない。

---

## 担当分担

### 1人目: 駐車場シーン

- `Waypoints` にテスト用ノードを置く
- テスト対象の `ParkingSlot` に `ApproachPoint` を置く
- `parkingPoint` の向きを確認する
- デバッグしやすい位置にカメラを調整する

### 2人目: 車・NPC

- `PathFollower.cs` を作る
- `NPCDriver.cs` を作る
- `ParkingAction.cs` を作る
- `DummyCar_001` に必要なコンポーネントを付ける

### 3人目: データ・Web

Phase 1では実装不要。  
ただし、後でAPIへ渡すために以下のイベント名だけ確認しておく。

- `reserved`
- `parking_started`
- `parked`
- `left`

### 4人目: 統合

- シーンとスクリプトの参照切れを確認する
- 実行時にConsoleエラーが出ないことを確認する
- Phase 1完了条件を満たしているか確認する

---

## 作業ブランチ案

```powershell
git checkout -b feature/npc-phase1
```

担当を分ける場合:

```powershell
git checkout -b feature/parking-waypoints
git checkout -b feature/npc-path-following
git checkout -b feature/npc-parking-action
```

シーン変更は衝突しやすいので、`SampleScene.unity` を同時に複数人で触らない。

---

## 完了条件

Phase 1完了は以下をすべて満たした状態。

- [ ] `DummyCar_001` が入口付近から動き始める
- [ ] `RoadNode` またはテスト用経路をたどって `ApproachPoint` へ向かう
- [ ] 目標の `ParkingSlot` が `Reserved` になる
- [ ] `ApproachPoint` 到着後に `parkingPoint` へ移動する
- [ ] 駐車完了後に `ParkingSlot.state` が `Occupied` になる
- [ ] ConsoleにNullReferenceExceptionが出ない
- [ ] 経路、目標枠、NPC状態がデバッグ表示またはConsoleで確認できる

---

## 今回やらないこと

以下はPhase 1ではやらない。

- 全240枠対応
- 複数台NPC
- 白線内判定
- 他車との衝突回避
- バック駐車/前向き駐車の自動判断
- 切り返し
- API/DB連携
- WebApp表示
- `WheelCollider` による本格車両物理
- AIによる警備員配置最適化

---

## 次のPhase

Phase 1が動いたら、次は以下の順番で進める。

1. Phase 2: 白線・衝突回避・駐車軌道
2. Phase 3: 複数台・予約・発生フロー
3. Phase 4: API/DB/WebApp連携
4. Phase 5: 最適化比較・ヒートマップ・警備員配置AI

---

## 実装時の注意

- 最初は小さく動かす。全枠対応は後回し。
- `ParkingSlot.state` は `Empty -> Reserved -> Occupied -> Empty` の流れで扱う。
- `Rigidbody` やColliderを大きく変更する場合は、既存の駐車検知に影響するため統合担当に確認する。
- シーン内参照はInspectorで見て、未設定ならConsoleで分かるようにする。
- 新規スクリプトは既存の命名に合わせて、過度な抽象化を避ける。
