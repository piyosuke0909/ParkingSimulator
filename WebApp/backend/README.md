# WebApp/backend

SmartParking MVP の FastAPI backend です。Unity WebGL から snapshot を受け取り、駐車場状態の集計、ユーザー案内、エリア予約、管理者向け AI 提案、fallback、ログ管理を担当します。

## 必要環境

- Python 3.13 系
- FastAPI
- Uvicorn
- Pydantic v2
- python-dotenv
- jsonschema（Unity同梱のP2/P3契約検証）

依存関係は `requirements.txt` で管理しています。

## セットアップ

```powershell
cd WebApp\backend
python -m pip install -r requirements.txt
Copy-Item .env.example .env
```

`.env`:

```env
CORS_ORIGINS=http://localhost:3000,http://127.0.0.1:3000
SMARTPARKING_LOCAL_API_KEY=local-dev-key
SMARTPARKING_SCHEMA_DIR=
GEMINI_API_KEY=
GEMINI_MODEL=gemini-3.5-flash
```

`.env` は backend 起動時に読み込まれます。値を変えた場合は backend を再起動してください。

## 起動

```powershell
python -m uvicorn main:app --host 127.0.0.1 --port 8000
```

確認:

```powershell
Invoke-RestMethod http://127.0.0.1:8000/api/health -Headers @{ "X-API-Key" = "local-dev-key" }
```

API仕様は `http://127.0.0.1:8000/docs` で確認できます。

## 主な API

| Method | Path | 内容 |
| --- | --- | --- |
| GET | `/api/health` | health check |
| POST | `/api/unity/snapshot` | Unity snapshot 受信 |
| POST | `/api/v1/snapshots` | Unity Snapshot v1.1 受信 |
| POST | `/api/v1/events` | Unity Event v1.0 NDJSON 受信 |
| GET | `/api/v1/commands` | Unity向けCommand取得 |
| GET | `/api/parking/status` | 駐車場状態 |
| GET | `/api/parking/recommendation` | ユーザー向け推奨エリア |
| POST | `/api/guidance/start` | エリア案内予約開始 |
| POST | `/api/guidance/cancel` | エリア案内予約キャンセル |
| GET | `/api/admin/state` | 管理者画面用状態 |
| GET | `/api/admin/areas` | area master |
| GET | `/api/admin/ai/status` | Gemini 設定状態 |
| POST | `/api/admin/ai/recommendations` | AI 提案生成 |
| POST | `/api/admin/commands` | 管理画面からSET_AREA_POLICYを作成 |

## 実装メモ

- 状態保存は MVP として in-memory
- 予約 TTL は 5 分
- 再案内 cooldown は 30 秒
- Unity snapshot は `sourceId + scene + sequenceNumber` で順序管理
- Unity向けv1 APIとhealth checkは `X-API-Key` でローカル認証
- Command結果は専用APIではなく `/api/v1/events` へP2 Event NDJSONで返す
- Snapshot/Event/Command Batchは `WebApp/MockBackend_P3/schemas` の正式JSON Schemaで検証
- schemaを別配置する場合は `SMARTPARKING_SCHEMA_DIR` でディレクトリを指定
- 最終 snapshot 受信から 5 秒で stale
- `GEMINI_API_KEY` 未設定、API失敗、JSON破損時は fallback report を返す
- AI には `areaStatus`, `alerts`, `availableGuards`, `operatorInstruction` のみ渡す
- 警備員の個人情報は Gemini に渡さない

## 確認コマンド

```powershell
python -m py_compile main.py models\schemas.py routers\admin.py services\state_store.py
```
