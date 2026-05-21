# Assets/Prefabs/Parking/

駐車スペース・区画の Prefab を管理するフォルダ。

## 主担当
**1人目**

## 想定するファイル

| ファイル名 | 内容 |
|-----------|------|
| `ParkingSpace.prefab` | 駐車スペース1区画 |
| `ParkingRow.prefab` | スペースを列にまとめたもの |

## Prefab に含める主なコンポーネント
- `BoxCollider` — 車の停車判定（Is Trigger: ON）
- `ParkingSpace.cs` — 空き・使用中の状態管理
- Tag: `ParkingSpace`（2人目のスクリプトが参照する）

## 2人目との取り決め
- Collider のサイズは車（`Car_Player.prefab`）より少し大きくする
- `spaceId` を Inspector から設定できるようにする（例: `A-01`, `B-03`）
