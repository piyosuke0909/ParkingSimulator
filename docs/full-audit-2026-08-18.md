# 全体監査・担当別引き継ぎ（2026-08-18、2026-08-19再検証）

## 監査基準

- GitHub全ブランチを確認
- 最新統合: `origin/develop` `2c8d18b`（2026-08-14 19:27 JST）
- 最新Unity P3: `6639aaa`（2026-08-14 16:40 JST）
- GitHub上の管理画面最終更新: `3341bf7`（2026-07-14 18:12 JST）
- `develop`とUnity P3のtree内容は同一
- SharePoint担当表はconnector未接続のため未照合

## 現在の全体フロー

```text
Unity Snapshot/Event
  -> FastAPI
  -> ユーザー画面 / 管理画面

管理画面 SET_AREA_POLICY
  -> FastAPI Command保存
  -> Unityが1秒Polling
  -> Unityが方針を適用
  -> P2 Command結果Event
  -> FastAPI状態更新
  -> 管理画面に成功・失敗を表示
```

## 担当別の進捗

### 1人目: 駐車場シーン

状態: Unity source上はP3統合済み。

- `SampleScene`にP3 Backend一式を構成済み
- A/B/C/D、ParkingSlot、WaypointをSnapshotへ出力可能
- 最新scene sourceには明確なmissing script参照を検出しなかった

残り:

- Unity Editor Playでシーン・車両・枠状態を最終目視
- 最新sourceからWebGL buildを再生成

### 2人目: 車・NPC / Unity通信

状態: source実装は完了寄り。

- Snapshot v1.1送信
- Event v1.0 NDJSON送信
- Command Batch v1.0受信
- source/session/run一致判定
- 期限・冪等性・最大キャッシュ管理
- `SET_AREA_POLICY`の適用とTerminal Event返却
- 方針は新規駐車先選択へ反映
- WebGL build時にP3構成を有効化し、旧Snapshot exporterを無効化
- 管理画面iframeをviewer mode、headless runnerをsender modeへ分離
- headless runnerのWebGLだけへローカルAPIキーを実行時注入

残り:

- Unity Editor PlayでBackend実装との実接続確認
- Unity Editor確認時は `SMARTPARKING_LOCAL_API_KEY=local-dev-key`をUnity project `.env`へ設定
- 最新WebGL buildを `WebApp/frontend/public/unity-build` へ反映

### 3人目: データ・Web

状態: 画面とMVP Backendは実装済み。永続化・分析は未実装。

完了:

- ユーザー画面のloading / ready / empty / error / stale
- 推奨エリア・枠・案内開始/中止
- 同一利用者の案内開始重複排除
- 管理画面の全体状況、マップ、方針、警備員、AI、ログ
- 管理画面のCommand操作と結果表示
- FastAPIのSnapshot/Event/Command/Admin API
- ローカルAPIキー認証
- Unity同梱の正式JSON SchemaによるSnapshot/Event/Command Batch検証

未完了:

- PostgreSQL等のDB接続、Repository、Migration
- 再起動後の予約・Command・Event状態復元
- DataAnalysisはREADMEとsample中心で、分析スクリプト本体なし
- ユーザー個人への再案内Commandはv1対象外

### 4人目: 統合

状態: コード上の契約統合とローカルHTTP/画面確認まで完了。

- Unity P3 schemaを正としてCommand契約を統一
- `sourceId`を含む対象判定へ統一
- 管理画面表示中SnapshotとCommand Polling対象の`sessionId`・`runId`一致を必須化
- CommandなしもBatch envelopeで200
- 10秒lease、最大3回、priority順へ統一
- 結果を専用APIではなくP2 `/api/v1/events`へ統一
- 古いWebGLでCommand Pollingがない場合、管理画面ボタンを安全に無効化
- Backend `.env` のローカルAPIキーをWebGL runnerへ渡す起動処理
- 表示用iframeの通信を停止し、別session/runによるCommand対象競合を防止

残り:

- Unity Editor/最新WebGLを使う最終E2E
- クラウド/DB担当成果との統合
- SharePoint担当表との最終照合

## クラウド移行の監査結果

GitHub内に以下は見つからなかった。

- Dockerfile / Compose
- CI/CD workflow
- Azure/GCP/AWS/Vercel設定
- Terraform/Bicep等のinfra定義
- DB model / Repository / Migration

したがって、GitHub上ではクラウド移行は未着手または未pushと判断する。担当表の記載と担当者のローカル作業はSharePoint接続後に再確認する。

クラウド担当へ渡す確定条件:

- Backend: FastAPI
- Frontend: Next.js
- Unity API: `/api/health`, `/api/v1/snapshots`, `/api/v1/events`, `/api/v1/commands`
- ローカル認証: `X-API-Key`
- 本番ではHTTPS、秘密情報管理、CORS、本番認証を決める
- Command/Event/予約/最新Snapshotを再起動後も復元できるよう永続化する

## 確認済み

- Backend自動テスト: 22件成功（冪等キーの全条件比較、Event整合性、Terminal状態の上書き防止、方針期限後の表示復帰、SnapshotとCommand対象の実行ID分離を含む）
- Frontend自動テスト: 9件成功
- TypeScript型確認: 成功
- Next.js production build: 成功
- Backend compile: 成功
- Unity P3 runtime設定とWebGL build処理: Unity 2022.3.62f2同梱compiler・実Unity参照で部分compile成功
- 実HTTP: 未認証health 401、認証済みhealth 200
- 実HTTP: Command Batch、管理画面作成、Unity形式Event 204、方針反映成功
- 一括起動: 明示環境変数を `.env` より優先し、Backend 200・管理画面 200・停止後プロセス残留なし
- 共通スモークテスト: Snapshot v1.1 → 空Batch → Command作成/取得 → succeeded Event → 管理画面反映まで成功
- 実ブラウザ: Command未接続時は操作無効、接続時は有効、結果成功表示まで確認

## 他担当待ちにできる項目

1. Unity担当: Editor Play確認と最新WebGL build
2. クラウド担当: 配置先、DB、永続化、本番認証、HTTPS/CORS
3. チーム管理: SharePoint担当表を開き、実担当と上記の割当を照合

提出物と合否条件は `docs/integration-handoff-checklist.md`、共通の実通信確認は `scripts/smoke-test-command-v1.py` を使用する。

この3点以外のローカル画面・Backend・Command v1の整合作業は本監査範囲で完了している。
