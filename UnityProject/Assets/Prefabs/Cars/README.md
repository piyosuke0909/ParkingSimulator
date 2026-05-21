# Assets/Prefabs/Cars/

車・NPC の Prefab を管理するフォルダ。

## 主担当
**2人目**

## 想定するファイル

| ファイル名 | 内容 |
|-----------|------|
| `Car_Player.prefab` | プレイヤーが操作する車 |
| `Car_NPC.prefab` | 自動で動く NPC の車 |

## Prefab に含める主なコンポーネント
- `Rigidbody` — 物理演算
- `Collider` — 衝突判定
- `CarController.cs` — 移動制御
- 車の 3D モデル（`Models/Cars/` から参照）

## 注意
- NPC・プレイヤー共通の処理は基底クラスに切り出すと管理が楽
- モデルは直接ここに入れず `Models/Cars/` を参照する
