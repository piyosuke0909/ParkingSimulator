# ユーザー画面担当 進捗

更新日: 2026-07-22

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

`CommandEnvelope`と`DomainEventEnvelope`の型・DDLはあるが、Command/Eventの配信・取得APIがない。
FrontendがCommand結果に応じて案内先と理由を更新するには、Backend担当から次が必要。

- Frontendが取得するCommand結果Eventのendpointまたは既存recommendation応答への反映方法
- `guidanceSessionId`の確定とMVP `reservationId`からの移行規則
- 再案内理由の`reasonCode`と利用者向けmessageの返却形式
- commandId/eventIdの重複排除・順序保証

### Backend契約で確認した不整合

`POST /api/guidance/start`を同じ`userSessionId`で連続実行すると、仕様上は409の想定だが、
現行Backendは200で複数予約を作成する。Frontendはsingle-flightで二重送信を抑止するが、
冪等性と二重開始拒否はBackend側でも実装が必要。
