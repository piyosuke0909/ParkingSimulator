# ユーザー画面 P0 表示項目・命名確認

## 目的

P0では、ユーザー画面が表示・送信する項目の名称と責任範囲を固定します。
現行MVPの通信名は変更せず、v1への移行時はBackend境界のadapterで変換します。
画面コード、Unityコード、現行API payloadの一括置換は行いません。

## 表示項目

| 画面表示 | 現行MVPの参照先 | v1 canonical | P0での扱い |
| --- | --- | --- | --- |
| おすすめエリア | `targetArea.areaId` / `targetArea.label` | `targetAreaId` | 表示名とIDを分離する |
| おすすめ駐車枠 | `recommendedSlotId` / `recommendedSlot` | `targetSlotId` | 画面内部で旧名を増やさない |
| おすすめ理由 | `targetArea.reason` | 要確認 | 表示文と機械判定用codeを分離する。OwnerはBackend |
| 再案内理由 | `replanReason` | Eventの`reasonCode`を参照 | 画面には利用者向け文言だけを表示する |
| ルート | `route.svgPath` / `route.steps` | Map/Waypoint契約からの派生表示 | Frontend表示用データと共有契約を分離する |
| 混雑 | `summary.congestionLevel` | `congestionLevel` | `low` / `medium` / `high`を日本語表示へ変換する |
| 有効空き | `summary.effectiveAvailable` | `emptyCount`等から算出 | 算出責任はBackend、表示責任はFrontend |
| 自車位置 | `assignedCar.mapPosition` | `vehicleId`と表示座標 | `displayCoordinateSystem`と`transformVersion`を併記できる設計にする |
| 最終更新 | `updatedAt` | 要確認 | Unityの`observedAt`とBackend受信時刻を分離し、画面だけJST表示する |
| 接続停止 | `stale` | Snapshot鮮度からの派生状態 | 利用者向け文言と内部判定を分離する |

## 操作名

| 画面表示 | 現行API | request | P0で固定する意味 |
| --- | --- | --- | --- |
| 案内を開始 | `POST /api/guidance/start` | `userSessionId`, `targetAreaId` | 新しい案内ライフサイクルを開始する |
| 案内を中止 | `POST /api/guidance/cancel` | `userSessionId`, `reservationId` | 現在の案内を取消する |
| 案内を再開始 | 開始APIを再利用 | `userSessionId`, `targetAreaId` | 中止・終了後に新しい案内を開始する |

`reservationId`から`guidanceSessionId`への移行はライフサイクル差を確認してから行い、
機械的に置換しません。

## 現行MVPからv1への対応

| 現行MVP | v1 canonical | 移行方針 |
| --- | --- | --- |
| `carId` | `vehicleId` | adapterで変換 |
| `cars` | `vehicles` | adapterで配列名を変換 |
| 車両の`state` | `vehicleState` | 対象を明示する |
| 枠の`state` | `slotState` | 対象を明示する |
| `reservedByCarId` | `reservedByVehicleId` | adapterで変換 |
| `occupiedByCarId` | `occupiedByVehicleId` | adapterで変換 |
| `sourceId` | `runtimeId` / `producerId` | 意味を確認し、機械置換しない |
| `scene` | `sceneName` | adapterで変換 |
| Snapshotの`timestamp` | `observedAt` | UTC RFC3339へ統一する |
| `isLeaving` | `slotState: "leaving"` | booleanとの二重管理を廃止する |
| `reservationId` | 移行先要確認 | `guidanceSessionId`へ機械置換しない |

## P0で変更しないもの

- `WebApp/frontend`の実装
- 現行MVP APIのpathとpayload
- `WebApp/backend/models/schemas.py`のMVPモデル
- UnityコードとUnity WebGL build
- BackendからUnityへのCommand実装
- DB接続と永続化

## P0完了条件

- [ ] おすすめ・理由・ルート・混雑・操作名の表示名が合意されている
- [ ] 表示名とcanonical名の対応をFrontend/Backend担当が確認している
- [ ] ID、UTC時刻、表示座標、enumの利用箇所が確認されている
- [ ] 未確定項目にOwnerが付いている
- [ ] `ScenarioFactor`と`DomainEvent`を別概念として説明できる
- [ ] 現行MVPを壊す一括置換が含まれていない

## 未確定事項

| 項目 | Owner | 確認内容 |
| --- | --- | --- |
| おすすめ理由のcanonical field | Backend | 表示文と`reasonCode`の返却方法 |
| 案内セッションID | Frontend + Backend | `reservationId`とのライフサイクル差 |
| 最終更新時刻 | Backend | Unityの`observedAt`とBackend受信時刻のどちらを表示するか |
| ユーザー画面の状態取得API | Frontend + Backend | `/api/admin/state`依存を続けるか、`/api/parking/status`へ分離するか |
| ルート表示契約 | Frontend + Backend | `svgPath`をMVP表示専用にし、v1 Map/Waypointから生成する境界 |
