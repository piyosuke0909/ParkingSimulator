# 統合担当待ち・受入チェックリスト

更新日: 2026-08-19

この文書は、Unity担当とクラウド担当が成果物を返したときに「動いた」の意味がずれないよう、統合完了の証拠を固定する。

## 共通の正本

- Command契約: `docs/architecture/command-receive-contract-v1.md`
- Snapshot/Event/Command Schema: `WebApp/MockBackend_P3/schemas/`
- Backend API実装: `WebApp/backend/routers/v1.py`
- Unity P3設定: `UnityProject/Assets/Scripts/P3/` と `UnityProject/Assets/Editor/P3BackendSetupWizard.cs`

口頭説明や古いWebGL buildではなく、上記ファイルと自動テスト結果を合否判定に使う。

## Unity担当の受入条件

### 提出物

- Unity 2022.3.62f2で再生成した `WebApp/frontend/public/unity-build/`
- build元commit ID
- Unity Editor Consoleにcompile errorがない画面またはログ
- 下記の実接続結果

### 必須確認

1. `SampleScene`をPlayすると `/api/v1/snapshots` が204になる。
2. `/api/v1/events` が `application/x-ndjson` で204になる。
3. `/api/v1/commands`を1秒間隔、5秒Timeout、最大10件で取得する。
4. `sourceId`、`sessionId`、`runId`がCommandのtarget 3項目と一致する。
5. 管理画面からAエリアを`PRIORITY`にすると、新規車両の選択へ反映される。
6. Unityが `command.succeeded` を返し、管理画面の最新Commandが「成功」になる。
7. 既存走行車両の目的地は変更されない。
8. WebGLではローカルAPIキーが実行時に渡され、Command取得が401にならない。
9. 旧 `UnityStateExporter` とP3 Snapshot送信が二重稼働しない。
10. 管理画面iframeが `backendMode=viewer` で通信せず、headless runnerだけが `backendMode=sender` としてSnapshot/Event/Command通信する。
11. 表示中SnapshotとCommand Polling元の`sessionId`・`runId`が一致する場合だけ管理画面のCommand操作が有効になる。別実行のSnapshotが混在した場合はBackendが409で拒否する。

現在コミット済みの旧buildはこの分離を含まない。一括起動スクリプトは旧buildを検出するとrunnerを安全側で停止するため、最新版を再buildするまでCommand E2E完了とは扱わない。

## クラウド担当の受入条件

### 先に確定して返す情報

- Frontend公開URL
- Backend公開URL
- 使用クラウドサービス
- DB種類と接続方式
- 本番認証方式
- secret管理場所
- CORS許可origin
- deploy方法とrollback方法

### 必須確認

1. HTTPSでFrontendとBackendへ接続できる。
2. Snapshot、Event、Command、予約がDBへ保存される。
3. Backend再起動後も未完了Commandと最新状態が復元される。
4. 同じ `eventId` / `snapshotId` の再送は重複登録されない。
5. 同じIDで異なる内容は409になる。
6. 本番の秘密情報をWebGL、URL、Git、ブラウザ公開環境変数へ含めない。
7. 許可していないoriginからのブラウザ通信を拒否する。
8. APIエラー詳細やstack traceを一般利用者画面へ表示しない。
9. deploy後に下記スモークテスト相当の一連通信が成功する。
10. 障害時のログ確認方法とDB backup/restore方法がREADMEへ記載されている。

## Command v1スモークテスト

ローカルBackendを起動して実行する。

```powershell
python scripts\smoke-test-command-v1.py
```

別URL・別ローカルAPIキーの場合:

```powershell
$env:SMARTPARKING_BASE_URL = "https://backend.example.com"
$env:SMARTPARKING_LOCAL_API_KEY = "設定済みの検証用キー"
python scripts\smoke-test-command-v1.py
```

テストする流れ:

```text
認証確認
  -> Snapshot v1.1
  -> 空Command Batch
  -> 管理画面Command作成
  -> Unity形式でCommand取得
  -> command.succeeded Event
  -> 管理画面状態へ成功反映
```

本番認証が `X-API-Key` 以外になる場合は、同じ通信内容を新しい認証方式で実行するスモークテストへ更新してから受入とする。

## 統合担当の最終判定

- Backend自動テスト、Frontend自動テスト、型確認、本番buildがすべて成功
- 最新WebGL buildでブラウザE2E成功
- クラウド配置後の再起動復元成功
- SharePoint担当表と実際の成果物を照合済み
- 上記4項目の証拠が揃うまでは「ソース実装済み」と「統合完了」を区別して報告する
