# SmartParking ParkingSimulator

Unity WebGL の駐車場シミュレーションから 1 秒ごとに snapshot を受け取り、FastAPI backend で混雑状況、空き台数、ユーザー案内、Gemini AI 提案を処理し、Next.js frontend でユーザー画面と管理者画面を表示する MVP です。

## 現在できていること

- 管理者画面に Unity WebGL を iframe 表示
- Unity WebGL から FastAPI backend へ snapshot JSON を送信
- backend で A/B/C/D エリア別の空き台数、混雑率、risk score を集計
- ユーザー画面で推奨エリア案内を表示
- 「案内開始」で 5 分 TTL のエリア予約を作成
- 管理者画面で通常マップ / ヒートマップを切り替え
- 管理者画面で AI 提案を手動生成
- Gemini 失敗時は fallback 提案を返す
- 管理者ログを in-memory で保持

## 構成

```text
Unity WebGL
  ├─ 管理者画面 iframe 内で映像を描画
  └─ 1秒ごとに FastAPI backend へ snapshot POST

FastAPI backend
  ├─ Unity snapshot 受信
  ├─ エリア集計 / risk score
  ├─ ユーザー案内 / 予約 TTL
  ├─ Gemini prompt / fallback
  └─ 管理者ログ

Next.js frontend
  ├─ ユーザー画面: /
  ├─ 管理者画面: /admin
  └─ /api/backend/* で FastAPI へ proxy
```

## フロントエンド / バックエンドフロー

### ユーザー画面

```text
User Browser
  -> Next.js /
  -> Next.js /api/backend/parking/recommendation
  -> FastAPI /api/parking/recommendation
  -> in-memory state / reservation
  -> Next.js
  -> User Browser
```

ユーザーが「案内開始」を押した時:

```text
User Browser
  -> Next.js /api/backend/guidance/start
  -> FastAPI /api/guidance/start
  -> 5分 TTL の area reservation 作成
  -> 推奨エリア、理由、ルート、空き台数を返す
```

### 管理者画面

```text
Admin Browser
  -> Next.js /admin
  -> iframe /unity-build/index.html
  -> Unity WebGL がブラウザ内で描画
```

状態データ:

```text
Unity WebGL
  -> POST http://localhost:8000/api/unity/snapshot
  -> FastAPI state store
  -> Next.js /api/backend/admin/state
  -> Admin Browser
```

AI 提案:

```text
Admin Browser
  -> Next.js /api/backend/admin/ai/recommendations
  -> FastAPI /api/admin/ai/recommendations
  -> Gemini API
  -> JSON validation
  -> AI proposal or fallback report
  -> Admin Browser
```

ポイント:

- frontend は画面表示と FastAPI への proxy を担当
- backend は Unity snapshot、状態集計、予約、Gemini、fallback を担当
- Unity 映像は backend から配信していない。WebGL build を frontend で直接表示している

## 必要環境

| 種類 | バージョン / 条件 |
| --- | --- |
| OS | Windows |
| Python | 3.13 系 |
| Node.js | npm が使える Node.js 環境 |
| Frontend | Next.js 16 / React 19 |
| Unity | 2022.3.62f2 |
| Unity module | WebGL Build Support |

## 初回セットアップ

### 1. backend

```powershell
cd WebApp\backend
python -m pip install -r requirements.txt
Copy-Item .env.example .env
```

`WebApp/backend/.env`:

```env
CORS_ORIGINS=http://localhost:3000,http://127.0.0.1:3000
GEMINI_API_KEY=
GEMINI_MODEL=gemini-3.5-flash
```

`GEMINI_API_KEY` が空の場合、AI 提案は fallback で返ります。`.env` を変更した場合は backend を再起動してください。

### 2. frontend

```powershell
cd WebApp\frontend
npm install
Copy-Item .env.example .env
```

`WebApp/frontend/.env`:

```env
BACKEND_URL=http://localhost:8000
```

## 起動方法

### 一括起動

backend と frontend をまとめて起動する場合:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-dev.ps1
```

起動後:

```text
ユーザー画面: http://127.0.0.1:3000
管理者画面:   http://127.0.0.1:3000/admin
API:          http://127.0.0.1:8000/api/health
```

停止:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\stop-dev.ps1
```

ログ:

```text
.logs/backend.log
.logs/frontend.log
```

初回起動時に `.env` がない場合は `.env.example` からコピーします。`WebApp/frontend/node_modules/` がない場合は `npm install` も実行します。

依存関係のインストールをスキップしたい場合:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-dev.ps1 -NoInstall
```

### 個別起動

ターミナルを 2 つ開きます。

### backend

```powershell
cd WebApp\backend
python -m uvicorn main:app --host 127.0.0.1 --port 8000
```

確認 URL:

```text
http://127.0.0.1:8000/api/health
http://127.0.0.1:8000/docs
```

### frontend

```powershell
cd WebApp\frontend
npm run dev -- --hostname 127.0.0.1 --port 3000
```

確認 URL:

```text
ユーザー画面: http://127.0.0.1:3000
管理者画面:   http://127.0.0.1:3000/admin
Unity WebGL:  http://127.0.0.1:3000/unity-build/index.html
```

FastAPI の `8000` は API 専用です。画面は Next.js の `3000` を開いてください。

## Unity WebGL build

MVP ではチームが clone 後すぐ管理者画面を確認できるように、`WebApp/frontend/public/unity-build/` の WebGL build 成果物も Git に含めます。

現在の WebGL build は Unity `2022.3.62f2` で生成済みです。

```text
WebApp/frontend/public/unity-build/
  index.html
  Build/
  TemplateData/
```

Unity 側では `BackendBridge` に `UnityStateExporter` を追加し、次の値を設定します。

```text
Backend Snapshot Url: http://localhost:8000/api/unity/snapshot
Source Id: unity-webgl-admin-01
Send Interval Seconds: 1
Include Slots: true
Include Cars: true
```

再 build する場合:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe' `
  -batchmode -quit `
  -projectPath 'C:\プログラム\SmartParking\ParkingSimulator\UnityProject' `
  -executeMethod SmartParkingWebGLBuild.Build
```

元の Unity project を Unity Editor で開いている場合、batchmode build は失敗します。その場合は Editor を閉じるか、`C:\tmp` に project をコピーして build してください。

## 主な API

| Method | Path | 内容 |
| --- | --- | --- |
| GET | `/api/health` | backend health check |
| POST | `/api/unity/snapshot` | Unity snapshot 受信 |
| GET | `/api/parking/status` | 駐車場状態 |
| GET | `/api/parking/recommendation` | ユーザー向け推奨エリア |
| POST | `/api/guidance/start` | エリア案内予約開始 |
| POST | `/api/guidance/cancel` | エリア案内予約キャンセル |
| GET | `/api/admin/state` | 管理者画面用状態 |
| GET | `/api/admin/areas` | area master |
| GET | `/api/admin/ai/status` | Gemini 設定状態 |
| POST | `/api/admin/ai/recommendations` | AI 提案生成 |

## Git に含めるもの / 含めないもの

含めるもの:

- backend / frontend の実装
- `README.md`
- `docs/`
- `WebApp/frontend/public/unity-build/`
- `WebApp/frontend/public/assets/`
- `WebApp/backend/.env.example`
- `WebApp/frontend/.env.example`

含めないもの:

- `.env`
- `node_modules/`
- `.next/`
- `*.log`
- `__pycache__/`
- Unity の `Library/`, `Temp/`, `Obj/`, `Build/`, `Builds/`, `Logs/`

現在の WebGL build は約 25MB です。将来 100MB を超えるファイルが出た場合は GitHub へ通常 push できないため、Git LFS または GitHub Releases に移してください。

## 確認コマンド

frontend:

```powershell
cd WebApp\frontend
npm run build
```

backend:

```powershell
cd WebApp\backend
python -m py_compile main.py models\schemas.py routers\admin.py services\state_store.py
```

## 残タスク

- Gemini API の 429 レート制限が落ち着いた状態で実生成確認
- エリア polygon の見た目最終確認
- 発表向け UI 文言とレイアウト調整
- 自動 AI 生成は後回し。現状は管理者の手動ボタンのみ

詳細な設計メモ:

- `docs/mvp-decisions-and-design-notes.md`
- `docs/realtime-ai-integration-plan.md`
