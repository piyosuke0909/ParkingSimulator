# ユーザー画面担当 進捗

更新日: 2026-08-18

## P0

- 表示項目・操作名・現行MVPからv1への命名対応は
  `../../docs/data-model/review/user-screen-p0-naming.md` に整理した。
- 現行MVP payloadは維持し、Frontend内部ではadapterでcanonical名へ変換する。
- `reservationId`と`guidanceSessionId`、おすすめ理由code、最終更新時刻の意味はBackend担当との合意待ち。

## P1 完了

- 初回loading、案内先なし、通信error、snapshot staleを独立状態として表示する。
- stale/error/empty時は新しい案内開始を抑止し、再読み込みを提供する。
- 案内中止はstale/error時も利用できる。
- おすすめ理由と再案内理由を利用者向けに表示する。
- Frontendの状態取得先を管理者用`/api/admin/state`から`/api/parking/status`へ分離した。
- `carId`、`recommendedSlotId`、`reservationId`等のMVP応答をFrontend adapterで扱う。
- 案内開始・中止はsingle-flightで二重送信を防ぐ。
- loading、ready、empty、error、staleの状態判定を自動テストで確認できる。

## 確認コマンド

```powershell
cd WebApp\frontend
npm.cmd test
npm.cmd run typecheck
npm.cmd run build
```

## 他担当待ち

### P2: 永続Backend

現行Backendはin-memoryで、Repository/DB接続がない。Frontendの実API接続は完了しているが、
再起動後の案内状態復元はBackend担当の永続化実装が必要。

### P3: Command結果による再案内

Command v1の配信とUnity結果Event受信は実装済み。管理画面のエリア方針変更はUnityまで往復できる。
ユーザー画面の「既に案内中の利用者を別の枠へ変更する」機能はv1対象外のため、次版で以下を確定する必要がある。

- `SET_AREA_POLICY`以外の再案内Command種別とpayload
- 既存recommendation応答へCommand結果を反映する規則
- `guidanceSessionId`の確定とMVP `reservationId`からの移行規則
- 再案内理由の`reasonCode`と利用者向けmessageの返却形式
- 利用者への通知・同意後に案内先を切り替える画面フロー

### Backend契約で確認した不整合

`POST /api/guidance/start`を同じ`userSessionId`で連続実行した場合は、
Backendが有効な既存予約を再利用する。Frontendのsingle-flightとBackendの重複排除の両方で二重開始を防ぐ。
