# Assets/Scripts/Vehicle/

車・NPC の動作に関するスクリプト群。

## 主担当
**2人目**

## 想定するスクリプト

| ファイル名 | 内容 |
|-----------|------|
| `CarController.cs` | 車の移動・ステアリング・物理制御 |
| `CarSpawner.cs` | 複数台の車を生成・管理する |
| `ParkingAction.cs` | 駐車・出庫の一連の動作 |
| `NPCDriver.cs` | NPC（自動）の運転AI |
| `PathFollower.cs` | 指定された経路を追従する |

## Phase 1 実装済み

| ファイル名 | 内容 |
|-----------|------|
| `PathFollower.cs` | `RoutePlanner` から受け取った座標列を順番に追従する |
| `NPCDriver.cs` | 空き枠を選び、予約し、走行と駐車をつなぐ |
| `ParkingAction.cs` | `ApproachPoint` 到着後に `ParkingPoint` へ入庫し、枠を `Occupied` にする |

Phase 1 では Transform ベースの低速移動を使う。`WheelCollider` や本格的な車両物理は Phase 2 以降で検討する。

## 他フォルダとの連携
- `Parking/` のスクリプトから駐車スペースの空き情報を受け取る
- `Network/` のスクリプトに駐車イベントを渡して API 送信させる
