# SmartParking P3 実Backend v1

Unity側でMock検証済みの以下の契約を、そのまま実Backendへ移すための基盤です。

- Snapshot: `smart-parking.simulation-snapshot` v1.1
- Event: `smart-parking.simulation-event` v1.0
- Command Batch: `smart-parking.command-batch` v1.0
- Command Type: v1.0では `SET_AREA_POLICY` のみ

## 実装済み

- `GET /api/health`
- `POST /api/v1/snapshots`
- `POST /api/v1/events` (`application/x-ndjson`)
- `GET /api/v1/commands`
- `POST /api/v1/admin/commands`（Frontend/管理画面からCommand登録）
- Command一覧・Runtime Target・最新Snapshot確認API
- PostgreSQL永続化
- Alembic Migration
- `snapshotId/eventId + payload hash` による重複判定
- Event Batch全体Transaction
- Command 10秒lease / 最大3回配信 / TIMED_OUT
- Terminal Command Eventによる状態確定
- Idempotency-KeyによるCommand重複作成防止
- API Key Header認証
- CORS
- Docker Compose
- ローカル結合試験用 `/admin`

## 最短の起動方法（Docker）

1. Docker Desktopを起動する。
2. `.env.example`を`.env`へコピーする。
3. `.env`の`POSTGRES_PASSWORD`を必要に応じて変更する。
4. PowerShellで `docker compose up --build` を実行する。
5. `http://127.0.0.1:8000/docs` を開く。
6. ローカル管理画面は `http://127.0.0.1:8000/admin`。

`start_docker.bat`でも起動できます。初回に`.env`が無い場合はテンプレートをコピーして停止します。

## Unity側

ローカル結合では概ね以下を使用します。

- Base URL: `http://localhost:8000`
- Authentication Mode: `ApiKeyHeader`
- Header: `X-API-Key`
- Unityプロジェクトルート `.env`: `SMARTPARKING_LOCAL_API_KEY=local-dev-key`

Backend側 `.env` の `SMARTPARKING_LOCAL_API_KEY` と一致させてください。

## 最初の結合試験

1. Backendを起動。
2. UnityをPlay。
3. Unityが `/api/v1/commands` をPollingする。
4. Snapshotが `/api/v1/snapshots` へ204で保存されることを確認。
5. `/admin`でRuntime Target取得。
6. `A / CLOSED` のCommandを登録。
7. UnityでAがCLOSEDになることを確認。
8. `/admin`のCommand状態が `SUCCEEDED` になることを確認。

## Command作成の制約

Backendは、対象 `sourceId/sessionId/runId` を現在のRuntime Targetとして認識し、かつそのRunのSnapshotが新鮮な場合だけCommandを作成します。初期値ではSnapshotの`generatedAtUtc`から120秒を超えるとstale扱いです。これは現行UnityのSnapshot間隔（60 simulation sec）に余裕を持たせるためです。

## 再配信

`GET /api/v1/commands`はPushではなくPullです。Unityが1秒Pollingします。

- 初回配信後10秒Terminal Eventなし: 再配信可能
- 最大3回
- 3回目からさらに10秒Terminal Eventなし: `TIMED_OUT`
- Backend再起動後もdelivery_count/last_delivered_at/statusはPostgreSQLから復元されます

## Snapshot/Event重複

- 新規ID: 保存して204
- 同じID + 同じpayload: 204
- 同じID + 異なるpayload: 409

Eventは1リクエスト全体を1Transactionとして扱うため、途中の1件だけ保存する部分成功は行いません。

## `.env`の役割

Unity用`.env`とBackend用`.env`は別ファイルです。

- Unity: API KeyなどUnity実行側の設定
- Backend: PostgreSQL接続、API Key、CORSなどサーバー側設定

`.env`はGitへ含めないでください。`.env.example`のみ共有します。

## WebGL/Frontendについて

`/admin`はローカル結合試験用です。本番Frontendでは固定API Keyをブラウザへ秘密情報として埋め込まないでください。最終公開時はユーザー認証/短期セッション等へ置換する前提です。

## 注意

旧 `/api/unity/snapshot` はP3の正式Endpointではありません。Unityの旧`BackendBridge / UnityStateExporter`はP3結合時OFFにしてください。

## 旧MVP Backendについて

以前のBackendにあった `/api/parking/status`, `/api/guidance/*`, `/api/admin/state`, Gemini連携などは、このP3基盤ではまだ統合していません。現在のFrontendがそれらを使用している場合、旧Backendを即時上書きせず、次工程で「P3 PostgreSQLデータを読む互換API」として必要なEndpointだけ移植してください。
