# DataAnalysis/notebooks/

Jupyter Notebook による探索的データ分析（EDA）用フォルダ。  
「どんなデータか調べる」「アルゴリズムを試す」段階で使う。

## 主担当
**3人目**

## 使い方
```powershell
jupyter notebook
# ブラウザが開いてノートブックを作成・実行できる
```

## ルール
- 本番コードはここに書かない（確認したら `scripts/` に移す）
- ノートブックは実行済み出力をクリアしてからコミットする
  （出力が大きくなると Git が重くなるため）

```powershell
# コミット前に出力をクリアするコマンド
jupyter nbconvert --ClearOutputPreprocessor.enabled=True --inplace *.ipynb
```
