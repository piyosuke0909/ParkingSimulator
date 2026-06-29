# Unity リアルタイム連携・Gemini AI 運営提案 手順書

作成日: 2026-06-28

## 前提

Unity 側は駐車場シミュレーションとして完成済みとして扱う。

目的は次の 2 つ。

- ユーザー画面: Unity から得た空き状況をもとに、駐車場所とルートを実質リアルタイムで案内する。
- 管理者画面: 現在の混雑、滞留、警備員シフトをもとに、どこへ警備員を配置すべきかを Gemini AI で提案する。

Gemini は常時呼び続けない。Unity の状態取得と AI 生成を分ける。

## 読み込んだデモ画面

ユーザー画面デモ:

```text
C:\Users\kyhktp0899\Downloads\U22-smartparking-user-demo\U22-smartparking-user-demo
```

確認できたこと:

- `index.html` 単体中心の静的デモ。
- `assets/parking.png` を駐車場マップとして表示。
- SVG overlay でルート線を描いている。
- `aiPlans` がフロント内にハードコードされている。
- `再案内` ボタンと AI 条件入力はあるが、backend / Gemini 連携はまだない。
- 現在は入力文から `nearEntrance`, `quiet`, `easy`, `nearExit`, `shortWalk` などの固定プランを選ぶデモ。

管理者画面デモ:

```text
C:\Users\kyhktp0899\Downloads\parkingfront (11)\parkingfront
```

確認できたこと:

- `index.html` が統合管理画面。
- `screens/` に画面別 HTML がある。
- `parking.png`, `heatmap.png` がある。
- `database/シフト.json` と `database/警備員個人情報.json` がある。
- 「AI運営アシスタント」と「警備員配置・動線提案」の UI 枠がすでにある。
- 現状の AI 生成はデモ処理で、実 Gemini API ではない。
- `window.postMessage` で Unity WebGL から `select-space` / `parking-data` を受け取る口がある。
- `unity-build/README.txt` に、Unity WebGL build を `unity-build/index.html` に置く想定と `postMessage` の形式が書かれている。

## モデル選定

Google 公式の Gemini API models ページでは、2026-06-15 更新時点で `Gemini 3.5 Flash` が stable として掲載されている。モデル文字列の例は `gemini-3.5-flash`。

運営提案の MVP は次でよい。

```text
GEMINI_MODEL=gemini-3.5-flash
```

考え方:

- 通常の運営提案、要約、警備員配置案は Flash で十分。
- 画像や動画を直接読ませるより、backend で集計した JSON を読ませる。
- より重い推論が必要な場合だけ上位モデルに切り替える。
- モデル名は `.env` で変更できるようにし、コードに直書きしない。

参考:

- https://ai.google.dev/gemini-api/docs/models

## 全体アーキテクチャ

```text
Unity
  |
  | 1秒ごとに JSON snapshot
  | 状態変化時に event
  v
Backend
  |
  | 状態保存、集計、推薦、AI生成の制御
  v
Frontend
  |
  | User: 駐車案内
  | Admin: 状況監視、警備員配置提案
```

重要な分離:

- Unity からのデータ取得は高頻度でよい。
- Gemini への生成依頼は低頻度にする。
- ユーザー向け駐車案内は基本的に backend のルール処理で即時返す。
- 管理者向けの説明文・警備員配置理由だけ Gemini に任せる。

## データ取得頻度

### Unity から backend

MVP 決定:

| 種類 | 頻度 | 内容 |
| --- | --- | --- |
| Snapshot | 1秒ごと | 全スロット、車両、集計、混雑スコア |
| Critical event | 即時 | 入庫、出庫、予約、滞留、満車、センサー異常 |
| Heatmap summary | 5秒から10秒ごと | エリア別混雑、滞留、流量 |
| Unity WebGL iframe | MVPで実施 | 管理者画面に Unity Play 映像を埋め込む |

Unity は毎フレーム送らない。`Update()` から直接送るのではなく、Coroutine で 1 秒間隔にする。

### backend から frontend

MVP 推奨:

| 画面 | 方式 | 頻度 |
| --- | --- | --- |
| ユーザー画面 | WebSocket / SSE | 状態更新時、または 1秒ごと |
| 管理者画面 | WebSocket / SSE | 状態更新時、または 1秒から3秒ごと |
| 初期表示 | REST | ページ読み込み時 |

frontend が直接 Unity を見に行かない。必ず backend を通す。

## Gemini を呼ぶタイミング

Gemini は常に呼ばない。

### 自動生成する条件

次のどれかが起きたら、backend が AI 生成候補を作る。

- エリア混雑率がしきい値を超えた。
- 5分以上、同じエリアが混雑し続けた。
- 入庫待ち車両が一定数を超えた。
- 出口付近の滞留が増えた。
- 警備員の休憩に入り、担当エリアが手薄になった。
- センサー異常や長時間滞在など alert が発生した。
- 管理者が「AIで生成」を押した。

### 呼び出し制限

MVP 推奨:

```text
同じ目的の AI 生成は 60秒から180秒の cooldown を置く。
```

例:

- 通常時: 5分に1回まで。
- 混雑悪化時: 1分に1回まで。
- 管理者の手動操作: 即時。ただし連打は 10秒 cooldown。
- 前回と入力データがほぼ同じなら、前回結果を再利用する。

### AI に渡すべきではないもの

- 警備員の住所
- 緊急連絡先
- 個人情報の全文
- 不要な過去ログ全件
- Unity の全オブジェクト詳細
- 動画そのもの

警備員配置に必要なのは、個人情報ではなく稼働状態と制約。

## JSON で渡す方針

JSON でよい。ただし Gemini に渡す JSON は、Unity 生データそのものではなく、backend で集計した運営判断用 JSON にする。

### Unity から backend

```json
{
  "timestamp": "2026-06-28T14:30:00+09:00",
  "scene": "SampleScene",
  "slots": [
    {
      "slotId": "A-01",
      "areaId": "A",
      "state": "Occupied",
      "sensorOccupied": true,
      "isLeaving": false,
      "position": { "x": 10.0, "y": 0.0, "z": 3.2 },
      "accessWaypointId": "WP-A-01",
      "reservedByCarId": null,
      "occupiedByCarId": "NPC_Car_001"
    }
  ],
  "cars": [
    {
      "carId": "NPC_Car_001",
      "state": "Parking",
      "position": { "x": 12.0, "y": 0.0, "z": 5.0 },
      "targetSlotId": "A-01",
      "isStoppedByFrontCar": false
    }
  ],
  "summary": {
    "empty": 120,
    "reserved": 8,
    "occupied": 105,
    "leaving": 4,
    "disabled": 3
  }
}
```

### backend からユーザー画面

```json
{
  "updatedAt": "2026-06-28T14:30:00+09:00",
  "recommendedSlot": {
    "slotId": "B-34",
    "areaId": "B",
    "reason": "入口から近く、周辺の空きが多い",
    "etaSeconds": 120
  },
  "route": {
    "routeId": "west-to-b34",
    "svgPath": "M28 246 H264 V178 H342",
    "steps": [
      "西入口から中央通路へ進む",
      "Bエリア接続通路で左折",
      "B-34 付近で徐行"
    ]
  },
  "summary": {
    "empty": 120,
    "congestionLevel": "normal"
  }
}
```

### backend から管理者画面

```json
{
  "updatedAt": "2026-06-28T14:30:00+09:00",
  "areas": [
    {
      "areaId": "A",
      "occupancyRate": 0.91,
      "waitingCars": 4,
      "stalledCars": 2,
      "riskLevel": "high",
      "recommendedGuardCount": 2
    }
  ],
  "guards": [
    {
      "guardId": "G01",
      "status": "active",
      "currentArea": "B",
      "shiftEnd": "18:00",
      "breakStart": "13:00",
      "breakEnd": "14:00",
      "canMove": true
    }
  ],
  "alerts": [
    {
      "type": "congestion",
      "areaId": "A",
      "severity": "high",
      "message": "A区画の混雑率が90%を超過"
    }
  ]
}
```

### backend から Gemini

```json
{
  "site": {
    "name": "第1駐車場",
    "timestamp": "2026-06-28T14:30:00+09:00"
  },
  "objective": "現在の混雑状況から警備員配置と動線を提案する",
  "constraints": [
    "警備員の個人情報は使わない",
    "休憩中の警備員は配置しない",
    "同じ警備員に離れたエリアを同時担当させない",
    "危険度 high のエリアを優先する"
  ],
  "areaStatus": [],
  "availableGuards": [],
  "activeAlerts": [],
  "previousRecommendation": {}
}
```

## Gemini の出力形式

自然文だけで返させない。backend では structured JSON を期待する。

```json
{
  "title": "A区画と出口側を優先した警備配置",
  "summary": "A区画の混雑率が高く、出口側にも滞留があるため、2名をA区画周辺、1名を出口側へ配置します。",
  "riskLevel": "high",
  "actions": [
    {
      "priority": 1,
      "areaId": "A",
      "action": "A区画入口に警備員を1名配置し、入場車をB区画へ分散誘導する",
      "reason": "A区画の空きが少なく、入場車が集中しているため"
    }
  ],
  "guardAssignments": [
    {
      "guardId": "G01",
      "targetArea": "A",
      "task": "A区画入口でB区画への分散案内",
      "durationMinutes": 20,
      "reason": "現在稼働中で、A区画に近い"
    }
  ],
  "operatorNotes": [
    "15分後にA区画の混雑率を再確認する"
  ]
}
```

frontend はこの JSON を表示する。表示できない形式なら backend で破棄し、前回の有効な提案を出す。

## backend のカスタムプロンプト方針

プロンプトは backend で組み立てる。

frontend から Gemini API を直接呼ばない。

理由:

- API key をフロントに置かない。
- prompt injection 対策を backend で行う。
- データ量を backend で削減できる。
- JSON schema validation を backend でできる。
- AI の呼び出し頻度を backend で制御できる。

プロンプト構成:

```text
System:
あなたは駐車場運営の管制支援AIです。安全を優先し、警備員の配置提案をJSONだけで返してください。

Developer:
出力 schema、禁止事項、優先順位、表現ルールを指定する。

User:
現在の集計JSON、管理者の追加指示、前回提案を渡す。
```

管理者が入力した自由文は、そのまま system prompt に混ぜない。必ず user instruction として扱う。

## ユーザー向け案内の考え方

ユーザー向けは、毎回 Gemini に聞かない。

基本は backend の deterministic logic で決める。

評価スコア例:

```text
score =
  空き枠であること
  + 入口からの距離
  + 周辺の混雑度
  + 出庫しやすさ
  + 歩行距離
  + 予約済みでないこと
  + 直近で状態が変わっていないこと
```

Gemini を使う場合:

- ユーザーが「入口近く」「停めやすい」「歩く距離短め」など自然文条件を入力したとき。
- 複数候補の説明文を自然に出したいとき。
- ルールで同点候補が多いとき。

ただし、最終的な slot reservation は backend が決める。AI の文章だけで枠を確定しない。

## 管理者向け AI の考え方

管理者向けは、AI の価値が高い。

AI に任せること:

- 混雑理由の説明
- 警備員配置の優先順位
- 動線の提案
- 管理者向けレポート文
- 次に見るべきポイント

AI に任せないこと:

- 生データの正誤判定
- 実際の警備員配置の強制変更
- 出退勤記録の書き換え
- 個人情報を使った判断

配置の反映は「AI提案を管理者が承認して反映」が安全。

## 生成タイミング設計

### 常時処理

backend が常にやること:

- Unity snapshot を受信する。
- 最新状態を保存する。
- エリア別混雑率を計算する。
- 空き枠推薦を更新する。
- 警告条件を判定する。
- frontend に状態を配信する。

### AI 生成処理

backend が必要なときだけやること:

- 状態が悪化したか判定する。
- cooldown を確認する。
- 前回と入力 JSON が大きく変わったか確認する。
- Gemini に集計 JSON を投げる。
- JSON schema を検証する。
- 管理者画面へ提案を配信する。

### 具体的なしきい値案

```text
area.occupancyRate >= 0.85
area.waitingCars >= 3
area.stalledCars >= 2
alert.severity == "high"
availableGuardCount < recommendedGuardCount
前回AI生成から 120秒以上経過
```

MVP では、まずは手動生成ボタンだけでもよい。その後、自動生成を追加する。

## 問題点・考えるべき点

### リアルタイム性

MVP の「実質リアルタイム」は 1秒更新で決定する。100ms 単位でやると実装と運用が重くなる。

注意:

- Unity の送信が詰まると Play に影響する。
- backend が落ちても Unity は動き続けられるようにする。
- stale data を検知する。例: 最終 snapshot から 5秒以上経ったら「Unity接続不安定」と表示。

### WebGL iframe と通信

MVP は管理者画面に Unity WebGL iframe を表示する。

残問題:

- Unity WebGL build が通るか。
- WebGL から backend へ `POST /api/unity/snapshot` できるか。
- CORS 設定が正しいか。
- Unity iframe が重すぎないか。

対策:

- WebGL build を優先して通す。
- build が失敗したら原因を特定し、Unity 側の設定・Package・Script・Shader を修正する。
- backend に CORS allowlist を設定する。
- MVP では `localhost` と管理者画面の origin を許可する。
- Unity 側の backend API URL は直書きせず、設定値にする。
- Unity からの snapshot は 1秒ごとに固定する。
- Unity iframe が失敗しても JSON 状態画面は動くようにする。
- Unity iframe の負荷は、まず動かして確認する。重かった場合に対策する。

### 駐車枠の競合

複数ユーザーに同じ空き枠を案内する可能性がある。

対策:

- MVP は枠ではなくエリア案内にする。
- backend にエリア予約 TTL を持たせる。TTL は5分固定。
- 案内開始時に `reserved` にする。
- 5分たっても到着しなければ予約解除。
- Unity の `Reserved` / `Occupied` と backend の予約状態を同期する。

### 座標変換の精度

Unity の slot 座標から area polygon を自動生成するが、画像上でズレる可能性がある。

対策:

- まず bounding box で area polygon を自動生成する。
- 作ってから slot marker を一時表示してズレを確認する。
- ズレる場合は calibration point を追加する。
- 必要なら手動補正 polygon に切り替えられる構造にする。

### AI のコストとレート制限

snapshot ごとに Gemini を呼ぶのは非現実的。

対策:

- AI 生成は手動またはしきい値イベント時のみ。
- cooldown を入れる。
- 同じ入力なら cache を使う。
- AI に渡す JSON を集計済みにする。
- 過去ログは直近 10件程度に絞る。

### AI の信頼性

AI はもっともらしい誤提案を出す可能性がある。

対策:

- JSON schema validation。
- backend 側で実行可能性チェック。
- 休憩中・退勤済み警備員の提案は破棄。
- 存在しない areaId / guardId は破棄。
- 壊れた JSON は fallback 提案を返す。
- AI提案は自動反映しない。管理者が判断するための表示だけにする。

### 個人情報

管理者デモには警備員個人情報 JSON があるが、AI には渡さない。

AI に渡してよいもの:

- `guardId`
- 稼働状態
- 現在エリア
- シフト時刻
- 休憩時刻
- 移動可否

AI に渡さないもの:

- 氏名
- 住所
- 緊急連絡先
- 性別

### 映像連携

映像は状態連携とは分ける。

MVP では管理者画面に Unity WebGL iframe を埋め込む。

ただし、駐車場状態の API は Unity 映像とは分離する。Unity iframe の読み込みに失敗しても、JSON 状態、マップ、AI提案は動くようにする。

WebGL iframe 以外の方式は後回しにする。

- screenshot / MJPEG / WebRTC は MVP では扱わない。
- ユーザー画面はルートと地図が重要で、動画は必須ではない。

## 地図座標とエリア polygon の自動生成

MVP では、フロント地図の A/B/C/D エリア範囲を Unity の `ParkingSlot` 座標から自動生成する。

流れ:

1. Unity から `ParkingSlot` 一覧を取得する。
2. `areaId` ごとに slot をまとめる。
3. `parkingPoint` または `transform.position` を Unity world 座標として使う。
4. `worldToMap()` で `parking.png` 上の座標へ変換する。
5. エリアごとの点群から polygon と center を作る。
6. frontend は polygon を使って通常マップ / ヒートマップ overlay を表示する。

MVP の polygon は bounding box でよい。

```text
areaPolygon = [
  [minX - padding, minY - padding],
  [maxX + padding, minY - padding],
  [maxX + padding, maxY + padding],
  [minX - padding, maxY + padding]
]
```

将来、見た目や精度が必要になったら convex hull または手動補正 polygon に切り替える。

出力例:

```json
{
  "generatedFrom": "unity-slots",
  "areas": {
    "B": {
      "center": { "x": 330, "y": 180 },
      "polygon": [[250, 110], [410, 110], [410, 250], [250, 250]],
      "slotCount": 60
    }
  }
}
```

### デモ画面との接続

管理者デモは `postMessage` の口があるため、短期デモでは Unity WebGL iframe から直接画面に送れる。

ただしMVP本実装では、Unity iframe の表示は管理者画面に埋め込み、状態データは Unity から backend へ送る。

短期デモ:

```text
Unity WebGL iframe -> postMessage -> admin index.html
```

本実装:

```text
Unity WebGL -> Backend -> WebSocket/SSE -> admin frontend
```

## 実装手順

### Phase 1: backend の状態サーバー

1. FastAPI を作る。
2. `POST /api/unity/snapshot` を作る。
3. 最新 snapshot を in-memory に保存する。
4. `GET /api/parking/status` を作る。
5. `GET /api/admin/state` を作る。
6. WebSocket または SSE で状態更新を配信する。

### Phase 2: Unity exporter

1. `UnityProject/Assets/Scripts/Network/UnityStateExporter.cs` を作る。
2. `ParkingLotManager` から slots を集める。
3. `NPC_CarController` / `Car` から cars を集める。
4. 1秒ごとに backend へ JSON POST する。
5. 失敗しても Unity Play を止めない。

### Phase 3: ユーザー画面接続

1. ユーザー画面の hardcoded `aiPlans` を backend response に置き換える。
2. `GET /api/parking/recommendation` を作る。
3. `recommendedSlot`, `route.svgPath`, `steps` を画面へ反映する。
4. 案内開始時に backend で reservation を作る。
5. slot が埋まったら自動で再案内する。

### Phase 4: 管理者画面接続

1. `parkingData.spaces` を backend の `GET /api/admin/state` から読み込む。
2. WebSocket/SSE で `spaces`, `guards`, `alerts` を更新する。
3. 既存の `AI運営アシスタント` フォームを `POST /api/admin/ai/recommendations` に接続する。
4. Gemini 結果を既存の `aiResultTitle`, `aiResultSummary`, `aiActionList`, `aiGuardPlanList` に流す。
5. AI 提案を承認して警備員配置へ反映する導線を作る。

### Phase 5: Gemini 連携

1. backend に Gemini client を追加する。
2. `.env` に `GEMINI_API_KEY` と `GEMINI_MODEL=gemini-3.5-flash` を置く。
3. prompt builder を backend に作る。
4. JSON schema validation を入れる。
5. cooldown / cache を入れる。
6. 失敗時は前回提案または fallback report を返す。

### Phase 6: 自動 AI 生成

1. エリア別 risk score を計算する。
2. しきい値を超えたときだけ AI job を enqueue する。
3. 同じ状態なら再生成しない。
4. 生成結果を admin に push する。
5. 管理者に「自動生成」「手動生成」「承認済み」を区別して表示する。

## MVP のおすすめ順

最短で動く順番:

- [x] backend を作る。
- [x] `POST /api/unity/snapshot` の最小受信を作る。
- [x] Unity WebGL から最小 snapshot exporter で疎通確認する。
- [x] snapshot に `sourceId`, `scene`, `sequenceNumber`, `timestamp` を入れる。
- [x] backend は古い `sequenceNumber` の snapshot を破棄する。
- [x] 管理者画面へ backend の固定 JSON を表示する。
- [x] Unity WebGL iframe を管理者画面に埋め込む。
- [x] Unity exporter で実データを送る。
- [x] backend に area master を持たせる。
- [x] Unity slot 座標から area polygon と center を自動生成する。
- [x] 管理者画面の駐車場マップを実データ化する。
- [x] ユーザー画面の推薦エリアを実データ化する。
- [x] エリア予約、5分 TTL、再案内 cooldown 30秒を入れる。
- [x] Gemini は管理者の手動ボタンから開始する。
- [ ] 自動生成は最後に入れる。

### 実装メモ 2026-06-29

- backend / frontend / Unity exporter のコード追加まで完了。
- backend への手動 `POST /api/unity/snapshot` は疎通確認済み。
- Unity WebGL からの実疎通は、Unity Editor 再コンパイル後に `UnityStateExporter` をシーンへ追加して確認する。
- Unity WebGL build 成果物は `WebApp/frontend/public/unity-build/` に置く。Git には生成物を含めない。
- 2026-06-29 追記: Unity `2022.3.62f2` に WebGL Build Support が入っていることを確認し、`C:\tmp\SmartParkingUnity2022Build_20260629144443` にコピーした project から正式 WebGL build を実行した。出力先は `WebApp/frontend/public/unity-build/`。元の `UnityProject` は Unity Editor で開かれていたため、同一 project の batchmode 起動は避けた。
- 2026-06-29 追記: 管理者画面で `Unity接続中` を確認済み。backend は `snapshotVersion=173`, `stale=false`, `source=unity-webgl-admin-01` の状態を返しており、Unity WebGL から backend への snapshot 送信は成立している。
- 2026-06-29 追記: 管理者画面とユーザー画面に `snapshotVersion`, 最終受信時刻、総台数、空き合計、全体混雑率を表示し、Unity実データが UI 判断に使われていることを確認しやすくした。
- 2026-06-29 追記: エリア polygon は MVP では画面上の見た目を優先し、A=左上、B=左下、C=右上、D=右下の固定象限配置に補正した。slot 数や混雑率は Unity snapshot 由来のまま使う。

## 追加 MVP 決定

- 再案内候補: `occupancyRate >= 0.85`
- 強制再案内: `occupancyRate >= 0.95`
- 再案内 cooldown: 同じユーザーには30秒。ただし満車時は即時。
- 推薦判定: `effectiveAvailable = emptyCount - activeAreaReservations`
- 予約期限切れ: frontend に「案内を更新」を表示し、backend は再推薦を返す。
- ヒートマップ: MVP は Unity heatmap texture を送らず、backend の area risk を overlay する。
- Gemini 入力: `areaSummary`, `alerts`, `availableGuards`, `operatorInstruction` のみ。
- Gemini に渡さない: slot list 全件、car list 全件、個人情報、全 snapshot。
- AI cache: `hash(areaStatus + alerts + availableGuards + operatorInstruction)` が同じなら前回結果を返す。
- ログに残さない: AI入力全文、個人情報、全 snapshot、全 slot list、全 car list。
- frontend: React / Next.js App Router を使う。
- Unity連携・Gemini・状態管理は FastAPI backend が担当し、Next.js は画面と中継を担当する。
- Unity build 配置: `WebApp/frontend/public/unity-build/`
- iframe src: `/unity-build/index.html`
- Unity WebGL build 成果物は Git 管理せず、生成手順だけ docs に書く。
- Gemini API key は backend `.env` のみに置く。
- backend 内部時刻は UTC、表示は Asia/Tokyo、JSON は ISO8601。
- MVP では本格的なセキュリティはまだ考えない。
- 状態保存は in-memory 開始。余裕があれば JSON file 保存。DB は後回し。
- 複数管理者画面は MVP では許容する。
- Unity sourceId は `unity-webgl-admin-01` 固定でよい。
- 最終 snapshot 受信から5秒で stale。復帰 snapshot が届いたら自動で正常扱いに戻す。
- frontend へ返す状態には `snapshotVersion` と `updatedAt` を含める。
- area polygon の歪みは MVP では許容し、必要時だけ calibration / 手動補正する。
- risk score は `occupancyRate`, `waitingCars`, `leavingCars`, `staleVehicles` の簡易式で作る。
- Gemini には `availableGuards` のみ渡す。不足時は「現有2名で優先配置」と書かせる。
- AI UI 文言は「提案」「参考」「管理者判断」とし、自動反映しない。
- 再案内時は短い理由を表示する。
- 案内キャンセル API として `POST /api/guidance/cancel` を用意する。ただし5分TTLで自然解除もできる。
- MVP では backend のエリア予約を Unity へ戻さない。
- AI生成中 UI と timeout fallback を用意する。

## 結論

状態取得と AI 生成を分けるのが重要。

Unity からのデータは 1秒ごとに取り続ける。backend は常に集計する。ユーザー案内は backend のルールで即時に返す。Gemini は管理者向けの説明・警備配置提案に使い、手動操作または混雑悪化などのイベント時だけ呼ぶ。

この方針なら、リアルタイム性、AI コスト、API 制限、個人情報、安全性を現実的に扱える。
