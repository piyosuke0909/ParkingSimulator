SmartParking P3 Mock Backend
============================

目的
----
Assets(8) の Unity P3 実装に合わせたローカル Mock Backend です。
本番 Backend / PostgreSQL / Docker が未完成でも、Unity 側の次の通信を確認できます。

  GET  /api/health
  GET  /api/v1/commands
  POST /api/v1/snapshots
  POST /api/v1/events

Command v1.0 は SET_AREA_POLICY のみを生成します。
Mock専用の操作画面は http://127.0.0.1:8000/mock です。

前提
----
- Windows: Python 3 がインストール済みで、py コマンドが使用できること
- Port 8000 が空いていること
- Unity P3BackendSettings の Base URL が http://localhost:8000 であること

起動（Windows）
----------------
1. このフォルダを任意の場所へ展開する。
2. start.bat をダブルクリックする。
3. 初回のみ .venv 作成と pip install が実行される。
4. "Uvicorn running on http://127.0.0.1:8000" と表示されたら起動完了。
5. ブラウザで http://127.0.0.1:8000/mock を開く。

PowerShell の場合は start.ps1 でも起動できます。

Unity側設定
-----------
P3BackendSettings を次の値にします。

  Base URL                  http://localhost:8000
  Health Endpoint           /api/health
  Snapshot Endpoint         /api/v1/snapshots
  Event Endpoint            /api/v1/events
  Command Endpoint          /api/v1/commands
  Source Id                 unity-webgl-admin-01
  Enable Command Polling    ON
  Command Polling Interval  1
  Command Request Timeout   5
  Command Maximum Count     10
  Authentication Mode       ApiKeyHeader
  API Key Header Name       X-API-Key
  API Key                   local-dev-key
  Treat Http 409 As Success OFF

Snapshot/Event送信も同時に試験する場合は Enable Snapshot Transmission / Enable Event Transmission を ON にします。

基本検証
--------
1. Mock Backendを起動する。
2. Unityで Tools > Smart Parking > P3 > Create or Update Backend Transmission Setup を実行する。
3. 上記API KeyをInspectorへ入力する。
4. Tools > Smart Parking > P3 > Validate Backend Transmission Setup を実行する。
5. UnityをPlayする。
6. 1～2秒後、Mock画面の Unity target に sourceId/sessionId/runId が表示されることを確認する。
7. Mock画面で Area=A, Policy=CLOSED を選び「Commandを登録」。
8. Unityが Commandを取得し、P3AreaPolicyRuntime のAが CLOSED になることを確認する。
9. CLOSED後に新規生成された車両がAへ新規割当されないことを確認する。
   既にAへ向かっている車両は強制変更されないのが正しい挙動。
10. Mock画面で該当Commandの terminalState が command.succeeded になることを確認する。
11. Area=A, Policy=NORMAL を送って通常状態へ戻ることを確認する。

PRIORITY / RESTRICTED
---------------------
PRIORITY と RESTRICTED は確率的な重み変更です。
1台だけ見て必ず目的地が変わるとは限りません。
複数台を生成し、parking_area.selected Eventの候補weightや選択傾向で確認してください。

再配信・重複防止
----------------
MockはCommand配信後10秒間は同じCommandを再配信しません。
Terminal Eventが届かず、有効期限内かつ配信回数3回未満なら同じ commandId/idempotencyKey で再配信します。
3回配信後も結果がない場合は TIMED_OUT にします。
Unity側は同じ idempotencyKey を二重実行しない想定です。

Backend停止・復旧
-----------------
1. Unity Play中にMock Backendを停止する。
2. Command Pollingが失敗し、2,4,8...最大30秒のバックオフになることを確認する。
3. Snapshot/Event送信がONなら P3Pending に未送信データが残ることを確認する。
4. Mock Backendを再起動する。
5. Polling成功後に1秒間隔へ復帰し、Pending Event/Snapshotが204応答後に減ることを確認する。

受信データ
----------
data/received_events.jsonl   Unityから受信したEvent
 data/snapshot_latest.json    最後に受信したSnapshot
 data/snapshots/              snapshotIdごとのSnapshot
 data/mock_state.json         Command/重複判定/受信履歴

Mock状態を消去する場合は操作画面の「Mock状態を全消去」を使います。

HTTP契約
--------
- X-API-Key: local-dev-key
- Commandなし: 200 + Command Batch + commands: []
- Snapshot成功: 204
- Event成功: 204
- JSON/NDJSON破損: 400
- ID同一・内容相違: 409
- サイズ超過: 413
- Schema不適合: 422
- Content-Type不正: 415
- API Key不正: 401

注意
----
- このMockはローカル検証専用です。クラウド公開しないでください。
- PostgreSQL、実際のトランザクション、クラウド認証、負荷分散等は再現しません。
- Command UIはMock専用で、本番API契約には含まれません。
- active targetが古いrunの未完了Commandで固定された場合はMock画面からResetしてください。
