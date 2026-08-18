# Command受信契約 v1.0（確定）

更新日: 2026-08-18

Unity側の `P3_Command_Contract_v1.schema.json`、`P3CommandPollingClient`、`P2SimulationEventPublisher` を正とし、管理画面・Backendもこの契約に統一する。

## 1. Command取得API

```http
GET /api/v1/commands?sourceId={sourceId}&sessionId={sessionId}&runId={runId}&limit={1..10}
X-API-Key: {local key}
```

`sourceId`、`sessionId`、`runId`は必須。`limit`は省略時10、最大10。

## 2. Polling

- 間隔: 1秒
- HTTP Timeout: 5秒
- 1回の最大取得件数: 10件
- 配信lease: 10秒

## 3. Commandなし

HTTP 200でCommand Batchを返す。

```json
{
  "contractName": "smart-parking.command-batch",
  "schemaVersion": "1.0",
  "serverTimeUtc": "2026-08-18T00:00:00Z",
  "leaseSeconds": 10,
  "commands": []
}
```

## 4. Command本体

必須:

- `contractName`, `schemaVersion`
- `commandId`, `idempotencyKey`, `commandType`
- `targetSourceId`, `targetSessionId`, `targetRunId`
- `createdAtUtc`, `expiresAtUtc`, `priority`, `payload`

任意: `correlationId`, `issuedBy`, `reason`。

## 5. ID

- `commandId`: Command自体の一意ID。Backendが生成する。
- `idempotencyKey`: 同一操作の二重実行防止キー。管理画面が指定可能で、未指定時はBackendが生成する。
- 同じ`idempotencyKey`かつ同じ内容（Payload、対象、期限秒数、priority、correlationId、issuedBy、reason）は既存Commandを返す。いずれかが異なる場合はHTTP 409。
- 再配信時は両IDを変えない。

## 6. 対象判定

Unityの現在値と `targetSourceId`、`targetSessionId`、`targetRunId`がすべて一致する場合だけ実行する。不一致は `command.rejected` / `TARGET_MISMATCH`。

BackendはUnityからのCommand Pollingで現在の3IDを認識する。管理画面は直近5秒以内にPollingが確認できた対象へCommandを作成し、対象不明時はHTTP 409とする。別実行向けの未完了Commandが残る間は対象を切り替えない。

管理画面の操作時は、表示中の最新Snapshotの`sessionId`・`runId`とCommand Polling対象の2項目も一致している必要がある。不一致・Snapshot更新停止・Snapshotに実行IDがない場合はCommandを作成せずHTTP 409とし、画面操作を無効にする。Snapshotの`sourceSystem`は固定値`unity`のため、Commandの`targetSourceId`との同一性判定には使用しない。

## 7. 期限切れ

`expiresAtUtc`を過ぎたCommandは実行しない。Unityが受信した場合は `command.expired` / `COMMAND_EXPIRED` を返す。Backendも配信前に期限切れへ更新し、再配信しない。

## 8. 返却・実行順

Backendは `priority` 降順、`createdAtUtc` 昇順、`commandId` 昇順で返す。Unityはレスポンス配列順に逐次実行する。

## 9. 完了タイミング

取得時点では完了にしない。次のTerminal EventをBackendが受信した時点で完了扱いとする。

- `command.succeeded`
- `command.failed`
- `command.rejected`
- `command.expired`

## 10. 再配信

- 配信後10秒間、Terminal Eventがなければ再配信対象
- 最大3回
- 同じ `commandId` と `idempotencyKey` を維持
- 期限切れ・完了済みは再配信しない
- 3回目の配信後10秒経過しても結果がなければBackend上は `timed_out`

## 11. 初期commandType

```text
SET_AREA_POLICY
```

## 12. Payload

```json
{
  "areaId": "A",
  "policy": "PRIORITY",
  "effectiveUntilUtc": "2026-08-18T00:05:00Z",
  "reason": "混雑緩和"
}
```

- 必須 `areaId`: `A`, `B`, `C`, `D`
- 必須 `policy`: `PRIORITY`, `CLOSED`, `RESTRICTED`, `NORMAL`
- 任意 `effectiveUntilUtc`: 方針自体の適用期限
- 任意 `reason`: 最大200文字
- `PRIORITY`: 新規車両の選択重みを3倍（Unity初期設定）
- `RESTRICTED`: 新規車両の選択重みを0.25倍
- `CLOSED`: 新規車両の選択重みを0
- `NORMAL`: 管理者指定の重み補正を解除
- 走行中車両の目的地は変更せず、Command受信後の新規駐車先選択から反映する
- `effectiveUntilUtc`到達後はUnityとBackend表示の双方が当該エリアを`NORMAL`として扱う

## 13. Unityから返す結果Event

既存のP2 Event APIへNDJSONで返す。結果専用Endpointは作らない。

```http
POST /api/v1/events
Content-Type: application/x-ndjson
X-API-Key: {local key}
```

Event種類:

- 途中経過: `command.accepted`, `command.started`
- Terminal: `command.succeeded`, `command.failed`, `command.rejected`, `command.expired`

`payload.command.status`はEvent種類に対応して、それぞれ `ACCEPTED`, `STARTED`, `SUCCEEDED`, `FAILED`, `REJECTED`, `EXPIRED` とする。Terminal Event受信後に遅れて届いた途中経過Eventは履歴として受理しても完了状態を上書きしない。同じCommandに異なるTerminal結果が届いた場合はHTTP 409とする。

`commandId`はEvent直下、`idempotencyKey`, `commandType`, `status`, `reasonCode`, `message`, `retryable`, `result`は `payload.command` に格納する。`result`は `areaId`, `previousPolicy`, `appliedPolicy` を持つ。

## 14. HTTP Status

- Command取得: 200、401（APIキー不正）、409（別Unity実行に未完了Command）、422（Query不正）、500（Backend生成Batchの契約違反）
- 管理画面Command作成: 201、409（対象Unityなし・冪等性衝突）、422（Payload不正）
- Snapshot受信: 204、400（JSON不正）、401、409（同一ID・異内容）、413（5 MiB超過）、415（Content-Type不正）、422（Schema不正）
- Event受信: 204、400、401、404、409、413、415、422

## 15. ローカル認証

- Header: `X-API-Key`
- 環境変数: `SMARTPARKING_LOCAL_API_KEY`
- ローカル初期値: `local-dev-key`
- 対象: `/api/health` とUnity向け `/api/v1/*`
- 本番キーをソースコードやWebGL buildへ埋め込まない。本番認証方式はクラウド担当と確定する。

## 関連する通信口

- `POST /api/v1/snapshots`: Unity Snapshot v1.1、204
- `POST /api/v1/events`: Unity Event v1.0 NDJSON、204
- `GET /api/v1/commands`: Unity Command Batch v1.0、200
- `POST /api/unity/snapshot`: 旧MVP互換
