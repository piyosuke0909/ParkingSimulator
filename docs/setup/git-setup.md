# Git セットアップ（全員必読）

---

## 1. 必要なツールをインストール

- [Git](https://git-scm.com/)
- [Git LFS](https://git-lfs.com/)（3Dモデル・画像ファイル管理に必要）

---

## 2. 初回セットアップ手順

```powershell
# Git LFS を有効化（初回のみ・全員実行）
git lfs install

# リポジトリをクローン
git clone <リポジトリURL> ParkingSimulator
cd ParkingSimulator
```

---

## 3. 毎日の作業フロー

```powershell
# 1. 作業前：最新を取得
git pull origin develop

# 2. 自分のブランチを作成（まだなければ）
git checkout -b feature/自分の担当名

# 3. 作業する

# 4. 変更をコミット
git add .
git commit -m "変更内容を一言で"

# 5. プッシュ
git push origin feature/自分の担当名
```

---

## 4. ブランチ命名ルール

| 担当 | ブランチ名 |
|------|-----------|
| 1人目（駐車場シーン） | `feature/parking-scene` |
| 2人目（車・NPC） | `feature/vehicle-npc` |
| 3人目（データ・Web） | `feature/data-web` |
| 4人目（統合） | `develop` に直接 or `fix/xxx` |

---

## 5. コミットメッセージの書き方

```
# 良い例
Add parking space prefab
Fix car collision detection
Update REST API endpoint for parking data

# 悪い例
修正
test
aaa
```

---

## 6. やってはいけないこと

- `main` ブランチに直接 push しない
- `.meta` ファイルを `.gitignore` に追加しない
- `Library/` フォルダをコミットしない（自動生成される）
