# Assets/Scripts/Network/

Unity から Web API（バックエンド）へデータを送信するスクリプト群。

## 主担当
**4人目**

## 想定するスクリプト

| ファイル名 | 内容 |
|-----------|------|
| `ApiClient.cs` | HTTP リクエストの送受信（UnityWebRequest） |
| `ParkingApiService.cs` | 駐車イベントを API に送る処理 |

## 送信先 API
```
POST http://localhost:8000/api/parking/event
```
詳細は [WebApp/backend/routers/](../../../../WebApp/backend/routers/README.md) を参照。

## 注意
- API の URL は `localhost:8000`（開発中）をハードコードせず、設定ファイルで切り替えられるようにする
- Unity の HTTP 通信は `UnityWebRequest` を使う（`HttpClient` は非推奨）
