# Assets/Scripts/Navigation/

駐車場内の道路ノードと経路生成に関するスクリプト群。

## Phase 1 実装済み

| ファイル名 | 内容 |
|-----------|------|
| `RoadNode.cs` | 通路上の地点。`nodeId` と接続先ノードを持つ |
| `RoadGraph.cs` | `RoadNode` を収集し、ノード間の経路を返す |
| `RoutePlanner.cs` | NPC の現在位置から駐車枠の `ApproachPoint` までの座標列を作る |

## 使い方

- Scene に `RoadGraph` を置く
- `Waypoints` 配下に `RoadNode` を置く
- 各 `RoadNode.connectedNodes` に次のノードを設定する
- `RoutePlanner.entranceNode` に入口ノードを設定する
- `ParkingSlot.roadNodeId` に近い道路ノードIDを入れる

Phase 1 では手置きノードでよい。自動生成や混雑コストは Phase 2 以降で扱う。
