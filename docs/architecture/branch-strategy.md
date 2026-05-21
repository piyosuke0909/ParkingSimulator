# ブランチ運用ルール

---

## ブランチ構成

```
main
 └── develop
      ├── feature/parking-scene   ← 1人目
      ├── feature/vehicle-npc     ← 2人目
      ├── feature/data-web        ← 3人目
      └── fix/バグ名              ← バグ修正（誰でも）
```

---

## 各ブランチの役割

| ブランチ | 役割 | push できる人 |
|---------|------|--------------|
| `main` | 常に動く状態 | 4人目のみ（PR 経由） |
| `develop` | 統合・確認用 | 4人目のみ（PR 経由） |
| `feature/*` | 各自の作業用 | 担当者 |
| `fix/*` | バグ修正用 | 誰でも |

---

## 通常の作業フロー

```
1. develop から feature ブランチを切る
   git checkout develop
   git pull origin develop
   git checkout -b feature/自分の担当

2. 作業・コミットを繰り返す
   git add .
   git commit -m "説明"

3. 作業が終わったら push して PR を作成
   git push origin feature/自分の担当
   → GitHub で "develop へのPR" を作成

4. 4人目がレビューしてマージ

5. 定期的に develop の最新を取り込む
   git merge origin/develop
```

---

## PR（プルリクエスト）のルール

- develop ブランチへのマージは必ず PR 経由
- レビュアーは **4人目** を指定する
- PR のタイトルは何をしたか分かるように書く
  - 良い例：`Add parking space collision detection`
  - 悪い例：`update`

---

## コンフリクト（競合）が起きたら

同じファイルを複数人が編集した場合に発生する。

```powershell
# develop の最新を取り込む
git fetch origin
git merge origin/develop

# コンフリクトしたファイルを手動で修正
# → VSCode で "Accept Current Change" / "Accept Incoming Change" を選ぶ

git add .
git commit -m "Resolve merge conflict"
```

シーンファイル（`.unity`）のコンフリクトは特に厄介なので、  
**シーンを大きく変更する前は Discord/Slack で一声かけること。**

---

## リリース（main へのマージ）のタイミング

- develop で全機能が動作確認できたとき
- 4人目が全員に確認を取ってから main にマージ

---

## よくある失敗と対処

| 失敗 | 対処 |
|------|------|
| main に直接 push してしまった | 4人目に連絡・一緒に対処 |
| develop から feature を切らずに作業した | `git rebase develop` で修正できる場合あり |
| `Library/` を commit した | `git rm -r --cached Library/` で取り消し |
