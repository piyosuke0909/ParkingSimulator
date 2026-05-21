# WebApp/backend/

Python（FastAPI）で作るバックエンド API サーバー。

## 主担当
**3人目**

## フォルダ構成

| フォルダ/ファイル | 内容 |
|----------------|------|
| `main.py` | アプリ起動・ルーター登録 |
| `routers/` | API エンドポイントの定義 |
| `models/` | データ構造の定義（Pydantic） |
| `services/` | ビジネスロジック（経路計算など） |
| `requirements.txt` | 依存パッケージ一覧 |

## 主な API エンドポイント

| メソッド | パス | 内容 | 呼び出し元 |
|---------|------|------|-----------|
| POST | `/api/parking/event` | 駐車イベントを受信 | Unity（4人目） |
| GET | `/api/parking/status` | 全スペースの空き状況 | React |
| GET | `/api/route/optimal` | 最適経路を返す | React |

## API ドキュメント
起動後に `http://localhost:8000/docs` で Swagger UI が確認できる。
