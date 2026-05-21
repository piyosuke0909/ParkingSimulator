# Unity 初期設定

Unity開発が初めての人向けの設定手順。チーム開発で競合が起きないための必須設定。

---

## 1. Unityのバージョン

チーム全員が **同じバージョン** を使うこと。  
→ バージョンは別途チームで決定してここに記載する。

推奨：[Unity Hub](https://unity.com/ja/download) からインストール。

---

## 2. プロジェクトを開いたら最初に行う設定

**Edit → Project Settings → Editor** を開く

| 項目 | 設定値 |
|------|--------|
| Version Control Mode | **Visible Meta Files** |
| Asset Serialization Mode | **Force Text** |

> この設定をしないと `.meta` ファイルが壊れてチーム間で競合が起きる。

---

## 3. Unity でプロジェクトを開く手順

1. Unity Hub を起動
2. `Add` → `ParkingSimulator/UnityProject/` フォルダを選択
3. 初回は `Library/` フォルダの再生成が走る（数分かかる）

---

## 4. .meta ファイルについて

Unityは全ファイルに `.meta` ファイルを自動生成する。

```
Car.fbx
Car.fbx.meta   ← これも必ず Git にコミットする
```

`.meta` ファイルを削除・無視すると参照が壊れてシーンが崩れる。  
**絶対に `.gitignore` に追加しないこと。**

---

## 5. よくあるエラー

| エラー | 原因 | 対処 |
|--------|------|------|
| `Library/` が重い | 初回生成中 | 待つ |
| シーンが壊れた | `.meta` ファイルが欠損 | Git から復元 |
| スクリプトが認識されない | Unity バージョン違い | バージョンを揃える |
