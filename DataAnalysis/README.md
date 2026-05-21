# DataAnalysis/

Unity のシミュレーションから得たデータを分析し、駐車場の最適化を行うフォルダ。

## 主担当
**3人目**

## フォルダ構成

| フォルダ | 内容 |
|---------|------|
| `scripts/` | 本番用の分析・最適化スクリプト |
| `notebooks/` | 探索的分析用の Jupyter Notebook |
| `data/samples/` | テスト用サンプルデータ |

## やること

1. Unity（2人目）から API 経由で受け取った駐車データを集計する
2. 時間帯ごとの混雑パターンを分析する
3. 入口から最寄りの空きスペースへの最適経路を計算する
4. 結果を `WebApp/backend/services/` に反映する

## 環境構築
```powershell
pip install -r requirements.txt
# Jupyter を使う場合
jupyter notebook
```
