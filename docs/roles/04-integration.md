# 4人目：統合担当

各メンバーの成果物を組み合わせて、全体として動く状態を維持する役割。

---

## 担当範囲

- `main` / `develop` ブランチの管理
- 各メンバーの PR レビュー・マージ
- 全体のシーン統合（各シーンを1つに組み合わせる）
- Unity ↔ API の連携確認
- 全体が動くかの動作確認

---

## 管理するもの

```
ParkingSimulator/
├── UnityProject/Assets/
│   └── Scenes/
│       └── Main.unity           ← 全体統合シーン（このファイルのオーナー）
├── docs/                        ← ドキュメント更新
└── .gitignore / .gitattributes  ← Git 設定
```

---

## 日常のワークフロー

```
各メンバーが feature/* ブランチで作業
        ↓
PR を作成 → 4人目がレビュー
        ↓
問題なければ develop にマージ
        ↓
全体動作確認 → main にマージ
```

---

## PR レビュー時のチェックリスト

- [ ] `.meta` ファイルが一緒にコミットされているか
- [ ] `Library/` や `Temp/` がコミットに含まれていないか
- [ ] Unity でシーンが正常に開けるか
- [ ] API の仕様変更があれば関係者に周知されているか
- [ ] コミットメッセージが分かりやすいか

---

## 統合シーン（Main.unity）の作り方

個別シーンを **Additive** ロードで組み合わせる：

```csharp
// SceneManager で別シーンを重ねて読み込む
SceneManager.LoadScene("ParkingLot", LoadSceneMode.Additive);
SceneManager.LoadScene("Vehicles", LoadSceneMode.Additive);
```

メリット：各担当が自分のシーンだけ触えばよく、Main.unity の競合が減る。

---

## 連携確認の手順

1. `develop` の最新を pull
2. Unity でシーンを開く
3. Play ボタンで実行
4. バックエンドを起動（`uvicorn main:app --reload`）
5. 車が動いて駐車イベントが API に届くか確認
6. フロントで駐車状況が反映されるか確認

---

## ブランチ運用

```
main        → 常に動く状態を保つ（直接 push 禁止）
develop     → 統合ブランチ（PR 経由でマージ）
feature/*   → 各メンバーの作業ブランチ
fix/*       → バグ修正
```

---

## 困ったときの対処

| 問題 | 対処 |
|------|------|
| マージ競合が起きた | シーンの担当者に連絡して一緒に解決 |
| Library が巨大なコミットに入っている | `git reset HEAD~1` して `.gitignore` を確認 |
| `.meta` ファイルが壊れた | `git checkout <hash> -- ファイルパス` で復元 |
| API と Unity の仕様がズレた | 2人目・3人目を呼んで仕様を再確認 |

---

## 注意事項

- `main` への push は必ず PR 経由にする
- 誰かの作業中のブランチに勝手に push しない
- 大きな構成変更（フォルダ移動など）は全員に事前に連絡する
