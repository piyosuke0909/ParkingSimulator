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

## 最新Frontend（2026-08-22）との互換API

最新版FrontendはNext.js Proxy経由で以下を使用します。

- `GET /api/admin/state`
  - 最新Snapshot由来の駐車場状態
  - `areaPolicies`
  - `commands`
  - `commandTarget`
  - `snapshotIdentity`
  - `commandTargetMatchesSnapshot`
- `POST /api/admin/commands`
  - Frontendは `targetSourceId/sessionId/runId` を直接指定しません。
  - Backendが最新Snapshotの `sessionId/runId` と一致し、かつ現在Polling中のRuntime Targetを自動選択します。
  - Smoke Test等の古いRuntime Targetを誤選択しないため、Snapshotと一致しないTargetにはCommandを作成しません。

FrontendのPOST例:

```json
{
  "commandType": "SET_AREA_POLICY",
  "idempotencyKey": "admin-A-CLOSED-...",
  "payload": {
    "areaId": "A",
    "policy": "CLOSED"
  }
}
```

レスポンスはFrontend表示用の `{ "command": ... }` 形式で、statusは小文字の
`pending/delivered/accepted/started/succeeded/failed/rejected/expired/timed_out` を返します。
正式なUnity向けCommand API (`/api/v1/commands`) と正式な管理API (`/api/v1/admin/*`) は変更していません。
