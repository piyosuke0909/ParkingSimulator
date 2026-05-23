# Assets/Scripts/AI/

駐車場シミュレーションにおける AI 関連スクリプトを管理するフォルダ。

## 主担当
**AI担当**

## 役割
- NPC の行動判断・意思決定 AI
- 駐車場内の混雑予測・自動最適化
- 経路探索アルゴリズムの Unity 側実装

## 想定するスクリプト

| ファイル名 | 内容 |
|-----------|------|
| `ParkingAI.cs` | 空きスペースへの誘導判断 |
| `CongestionPredictor.cs` | 時間帯ごとの混雑を予測 |
| `PathfindingAgent.cs` | 最適経路を Unity 上で実行する |

## 他フォルダとの連携

| 連携先 | 内容 |
|--------|------|
| `Scripts/Vehicle/` | NPC の移動指示を渡す |
| `Scripts/Parking/` | 空きスペース情報を受け取る |
| `Scripts/Network/` | バックエンドの最適経路結果を受け取る |

## 備考
- バックエンド側の経路計算（`WebApp/backend/services/route_optimizer.py`）と役割分担を明確にする
- Unity 内でリアルタイム判断が必要なものはここに、重い計算はバックエンドに寄せる
