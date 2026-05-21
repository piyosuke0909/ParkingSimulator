# DataAnalysis/scripts/

本番利用する Python 分析スクリプトを管理するフォルダ。  
検証が終わったロジックを `notebooks/` から移植してここに置く。

## 主担当
**3人目**

## 想定するスクリプト

| ファイル名 | 内容 |
|-----------|------|
| `analyze_parking.py` | 駐車データの集計・混雑パターン分析 |
| `optimize_route.py` | 最適経路計算（Dijkstra / A*） |
| `export_space_map.py` | 1人目のシーン座標からグラフを生成 |

## スクリプトの流れ

```
data/samples/ のデータを読み込む
    ↓
分析・計算
    ↓
結果を WebApp/backend/services/ に反映
```
