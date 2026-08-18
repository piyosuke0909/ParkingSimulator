# Frontend接続の最小仕様

## ローカル開発

管理FrontendからBackendへCommand登録する入口:

`POST /api/v1/admin/commands`

Headers:

- `Content-Type: application/json`
- `X-API-Key: <local key>` ※ローカル専用
- `Idempotency-Key: <操作ごとのUUID>`

Body例:

```json
{
  "targetSourceId": "unity-webgl-admin-01",
  "targetSessionId": "unity-session-...",
  "targetRunId": "unity-session-...-run-0001",
  "commandType": "SET_AREA_POLICY",
  "priority": 0,
  "issuedBy": "admin-ui",
  "expiresInSeconds": 30,
  "payload": {
    "areaId": "A",
    "policy": "CLOSED",
    "reason": "local integration test"
  }
}
```

現在のUnity実行対象は `GET /api/v1/admin/runtime-targets` で取得できます。
Command結果は `GET /api/v1/admin/commands/{commandId}` またはCommand一覧で確認できます。

## 本番WebGL

固定API Keyをブラウザへ秘密として配布しないこと。本番認証方式を確定するまでは、上記Admin APIはローカル/閉域結合試験用として扱ってください。
