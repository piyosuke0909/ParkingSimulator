# P3 PostgreSQL 保存構造

## simulation_snapshots
Snapshot v1.1を保存します。`snapshot_id`はUnique、全文は`payload JSONB`、重複判定用に`payload_hash`を保持します。

## simulation_events
Event v1.0を保存します。`event_id`はUnique、全文は`payload JSONB`。Command Event検索用に`command_id`も列として保持します。

## runtime_targets
Unityの現在実行を`source_id`単位で管理します。Command Pollingから`session_id/run_id`を把握し、Snapshotの`generatedAtUtc`を使ってCommand発行可否のfreshnessを判断します。

## commands
Command Type共通テーブルです。`command_type + payload JSONB`とし、将来Command Type追加時にテーブルを増やさなくてよい構成です。

主な状態:

- CREATED
- DELIVERED
- ACCEPTED
- STARTED
- SUCCEEDED
- FAILED
- REJECTED
- EXPIRED
- TIMED_OUT

`delivery_count`, `last_delivered_at_utc`, `terminal_event_id`, `result_payload`をDBへ保存するため、Backend再起動後も再配信判断を継続できます。
