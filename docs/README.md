# 次世代駐車場シミュレーター

「車をどこに停めたか分からない」を解決するための駐車場管理システム。  
Unityでシミュレーションを行い、得られたデータをWeb上で可視化・分析する。

---

## プロジェクト概要

```
UnityProject/   ← シミュレーション本体（1・2・4人目）
DataAnalysis/   ← データ解析・最適経路計算（3人目）
WebApp/         ← フロントエンド＋バックエンド（3人目）
docs/           ← このドキュメント（全員）
```

## システム全体像

```
[Unity シミュレーション]
        ↓ REST API
[Python バックエンド (FastAPI)]
        ↓
[React フロントエンド]
        ↓
[ブラウザで駐車状況・最適経路を表示]
```

---

## メンバーと役割

| 担当 | 役割 | 主なファイル |
|------|------|------------|
| 1人目 | 駐車場シーン構築 | `UnityProject/Assets/Scenes/`, `Models/`, `Prefabs/` |
| 2人目 | 車・NPC制御 | `UnityProject/Assets/Scripts/Vehicle/`, `Scripts/NPC/` |
| 3人目 | データ解析・Web | `DataAnalysis/`, `WebApp/` |
| 4人目 | 統合・全体管理 | 全体 |

---

## ドキュメント一覧

### セットアップ
- [Gitセットアップ](setup/git-setup.md) ← **全員最初に読む**
- [Unity初期設定](setup/unity-setup.md)

### 役割別ガイド
- [1人目：駐車場シーン担当](roles/01-parking-scene.md)
- [2人目：車・NPC担当](roles/02-vehicle-npc.md)
- [3人目：データ・Web担当](roles/03-data-web.md)
- [4人目：統合担当](roles/04-integration.md)

### アーキテクチャ
- [フォルダ構成](architecture/folder-structure.md)
- [ブランチ運用ルール](architecture/branch-strategy.md)
- [NPC駐車判断データ設計](architecture/npc-parking-decision-data.md)
- [NPC Phase 1 実装ハンドオフ](architecture/npc-phase1-implementation-handoff.md)
