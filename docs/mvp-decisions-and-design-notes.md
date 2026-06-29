# MVP 仕様決定メモと追加設計論点

作成日: 2026-06-28

## 今回決めたこと

### ユーザー案内は「枠」ではなく「エリア」

MVP では `B-34` のような個別駐車枠を指定しない。

ユーザーには次の粒度で案内する。

```text
Bエリアへ案内
Aエリアは混雑しているためCエリアへ案内
入口から近いBエリアを推奨
```

理由:

- 複数ユーザーに同じ枠を案内する競合を避けやすい。
- Unity 側のセンサー状態と backend の予約状態が多少ズレても破綻しにくい。
- ユーザー体験としても「まずエリアへ誘導」で十分デモとして成立する。
- 将来、枠単位の精密案内へ拡張できる。

ただし backend の内部では、将来のために `recommendedSlotId` を optional で持てる構造にする。

```json
{
  "targetAreaId": "B",
  "recommendedSlotId": null,
  "guidanceLevel": "area"
}
```

### 案内開始ボタンで予約する

ユーザーが「案内を開始」を押した時点で backend に予約を作る。

MVP では枠予約ではなく、エリア予約として扱う。

```json
{
  "reservationId": "res_001",
  "userSessionId": "user_abc",
  "targetAreaId": "B",
  "status": "active",
  "expiresAt": "2026-06-28T14:40:00+09:00"
}
```

推奨 TTL:

```text
5分
```

予約の扱い:

- ユーザーが案内開始したら `area guidance reservation` を作る。
- そのエリアの空き台数から案内中ユーザー数を差し引いて推薦する。
- TTL を過ぎたら自動解除。
- Unity の実状態が大きく変わったら再案内する。

### 将来は Unity 優先で再案内

今後の方針として、Unity と backend の状態がズレた場合は Unity を優先する。

例:

- backend は Bエリアを案内中。
- Unity では Bエリアが満車に近づいた。
- backend は予約を維持しつつ、ユーザーへ Cエリアへ再案内を出す。

優先順位:

```text
Unity 実状態 > backend 予約状態 > AI提案文 > frontend 表示
```

### Unity snapshot は 1秒ごと

Unity から backend への snapshot 送信間隔は MVP では 1秒ごとにする。

```text
Unity -> Backend: 1秒ごとに snapshot POST
```

理由:

- 駐車場案内では 1秒更新で実質リアルタイムとして十分。
- 毎フレーム送信より Unity Play への負荷が低い。
- backend / frontend の実装が単純になる。
- Gemini 生成とは分離できる。

補足:

- 入庫、出庫、満車、センサー異常などの重要イベントは必要なら即時 event として送る。
- 管理者画面では最終 snapshot から 5秒以上経ったら stale data として警告する。

### 管理者 AI は手動ボタンで生成

MVP では自動生成しない。

管理者が「AIで生成」を押した時点で backend が Gemini を呼ぶ。

AI に渡すのは、Unity 生データではなく backend が整形した小さい JSON。

backend が数値でやること:

- エリア別混雑率
- 滞留数
- 待機車両数
- 空き台数
- リスクレベル
- 推奨警備員数
- 稼働中警備員数

Gemini がやること:

- 管理者向けの説明文
- 警備員配置の提案
- 動線の提案
- 注意点の文章化

AI は配置を自動反映しない。提案だけ表示する。

### 管理者判断を優先する

警備員配置は AI が決めるのではなく、管理者が判断する。

MVP の UI は次まで。

- AI提案を表示
- 提案理由を表示
- 警備員ごとの推奨配置を表示
- ログに残す

配置データの自動書き換えはしない。

### 管理者画面に Unity Play 映像を表示する

管理者画面では Unity の Play 映像を表示する。

同時に、通常マップとヒートマップを切り替えられるようにする。

MVP では WebGL iframe 方式で進める。

```text
管理者画面 index.html
  └─ iframe で Unity WebGL build を表示
```

画面イメージ:

```text
[Unity Play 映像]
[通常マップ / ヒートマップ 切り替え]
[エリア別混雑]
[AI運営アシスタント]
[ログ]
```

### fallback とログを入れる

最低限の fallback は MVP に入れる。

必要な fallback:

- Unity からデータが来ない。
- Gemini API が失敗する。
- Gemini の JSON が壊れている。
- Gemini が存在しない areaId / guardId を返す。
- backend が最新状態を持っていない。

管理者ログに残すもの:

- Unity snapshot 受信時刻
- stale data 発生
- AI生成開始
- AI生成成功
- AI生成失敗
- fallback 使用
- 管理者が AI 提案を表示した時刻

## WebGL と Windows standalone の違い

### WebGL

Unity をブラウザ上で動かす方式。

管理者画面の中に iframe で埋め込める。

```text
admin index.html
  └─ iframe: unity-build/index.html
```

メリット:

- 管理者画面に Unity Play 映像を出しやすい。
- 既存の管理者デモに `unity-build/README.txt` があり、WebGL 埋め込み前提の口がある。
- `postMessage` で frontend とやり取りしやすい。
- デモとして見せやすい。
- ユーザーはブラウザだけで見られる。

デメリット:

- Unity WebGL build が必要。
- ブラウザ制約がある。
- CORS / HTTPS / iframe 周りの調整が必要。
- Windows ネイティブ機能やローカルプロセス管理はやりにくい。
- 重いシーンだとブラウザでの性能確認が必要。

向いているケース:

- 発表・デモで管理者画面に Unity 映像を出したい。
- ブラウザ完結にしたい。
- まず見える MVP を作りたい。

### Windows standalone

Unity を Windows アプリとしてビルドして、backend から起動する方式。

```text
Backend
  └─ ParkingSimulator.exe
```

メリット:

- Unity の実行が安定しやすい。
- ローカル実行やプロセス制御を backend がしやすい。
- 将来的にカメラ、ファイル、外部機器、重い処理を扱いやすい。
- WebGL 制約を受けにくい。

デメリット:

- 管理者画面へ映像を出すには別途 streaming / screenshot / MJPEG / WebRTC が必要。
- Windows 環境依存になる。
- backend からのプロセス起動・停止・監視が必要。
- デモ環境のセットアップが WebGL より重い。

向いているケース:

- backend が Unity を起動・停止したい。
- 将来的に実機連携や重いシミュレーションを想定する。
- ブラウザ埋め込みより安定性を優先する。

### MVP の決定

今回の希望は「管理者側に Unity の Play 映像を表示」なので、MVP は WebGL iframe 方式で進める。

MVP 構成:

```text
Unity WebGL
  ├─ 管理者画面に iframe 表示
  └─ backend へ 1秒ごとに snapshot POST

Backend
  ├─ 状態集計
  ├─ ユーザー案内
  ├─ 管理者状態API
  └─ Gemini 手動生成

Frontend
  ├─ ユーザー画面
  └─ 管理者画面
```

ただし、Unity WebGL build が重すぎる・動かない場合は Windows standalone に切り替える。

その場合の管理者映像は後回しにして、まず JSON 状態とマップ表示を優先する。

## Unity 座標とフロント地図座標の変換

できます。

ただし、変換には基準が必要。

確認済み:

```text
ユーザー画面 parking.png: 637 x 492
管理者画面 parking.png: 637 x 492
管理者画面 heatmap.png: 637 x 492
```

画像サイズが共通なので、1つの変換設定をユーザー画面と管理者画面で共有できる。

### 基本方針

Unity の `x,z` 座標を画像上の `pixelX,pixelY` に変換する。

Unity 側のヒートマップ設定には次の値がある。

```text
worldMinX = -180
worldMaxX = 180
worldMinZ = -150
worldMaxZ = 150
flipHeatmapX = true
flipHeatmapZ = false
```

初期変換式の案:

```text
normX = (unityX - worldMinX) / (worldMaxX - worldMinX)
normZ = (unityZ - worldMinZ) / (worldMaxZ - worldMinZ)

if flipX:
  normX = 1 - normX

if flipZ:
  normZ = 1 - normZ

pixelX = normX * imageWidth
pixelY = normZ * imageHeight
```

ただし、画像の上下方向と Unity の Z 軸方向が完全一致しているかは実画面で確認する必要がある。

### Codex 側でやること

Codex 側で実装できること:

- Unity の slot / area / waypoint 座標を読み出す。
- `parking.png` の画像サイズを取得する。
- 座標変換用 JSON を作る。
- backend に `worldToMap()` を実装する。
- frontend に `mapX,mapY` を渡す。
- 画像上にエリア polygon / marker / heatmap を重ねる。
- 実画像を見ながら補正値を調整する。

必要なもの:

- Unity 座標上の基準点。
- 画像上の対応点。

最低 2 点でも線形変換はできるが、向きの確認を含めるなら 4 点ほしい。

例:

```json
{
  "image": { "width": 637, "height": 492 },
  "calibrationPoints": [
    {
      "label": "westEntrance",
      "unity": { "x": -180, "z": 0 },
      "image": { "x": 28, "y": 246 }
    },
    {
      "label": "eastEnd",
      "unity": { "x": 180, "z": 0 },
      "image": { "x": 610, "y": 246 }
    }
  ]
}
```

### MVP での座標変換の粒度

MVP は枠ではなくエリア案内なので、個別 slot の精密な位置合わせは不要。

まず作るべきもの:

- A/B/C/D のエリア polygon
- 各エリアの代表点 `center`
- 入口の代表点
- ルート表示用の簡易 SVG path

MVP では、A/B/C/D のエリア polygon と代表点を Unity の `ParkingSlot` 座標から自動生成する。

自動生成の流れ:

```text
1. Unity から全 ParkingSlot を取得する
2. slot.areaId ごとに groupBy する
3. 各 slot の parkingPoint または transform.position を Unity world 座標として取得する
4. worldToMap() で画像座標へ変換する
5. areaId ごとの点群から polygon を作る
6. 点群の平均値または polygon の中心を代表点 center にする
7. polygon に padding を加えて、見た目上のエリア範囲にする
```

polygon の MVP 実装は、まず bounding box でよい。

```text
minX / minY / maxX / maxY を求めて四角形を作る
```

その後、必要なら convex hull や手動補正 polygon に拡張する。

自動生成する JSON 例:

```json
{
  "generatedFrom": "unity-slots",
  "image": { "width": 637, "height": 492 },
  "areas": {
    "A": {
      "label": "Aエリア",
      "center": { "x": 160, "y": 365 },
      "polygon": [[70, 290], [250, 290], [250, 440], [70, 440]],
      "slotCount": 60
    }
  }
}
```

例:

```json
{
  "areas": {
    "A": {
      "label": "Aエリア",
      "center": { "x": 160, "y": 350 },
      "polygon": [[80, 300], [240, 300], [240, 430], [80, 430]]
    },
    "B": {
      "label": "Bエリア",
      "center": { "x": 330, "y": 180 },
      "polygon": [[260, 120], [400, 120], [400, 240], [260, 240]]
    }
  }
}
```

今後、枠案内に拡張するときに slot の `parkingPoint` を同じ変換で画像上へ出す。

手動で決めるのは次のものだけにする。

- Unity world 座標と画像座標を合わせる calibration point
- polygon の見た目補正
- 入口・出口など、slot だけでは表せない代表点

## エリア定義

### A/B/C/D 固定でよいか

MVP は A/B/C/D 固定でよい。

Unity シーンにも `areaId` として A/B/C/D が入っている。

ユーザー向けには A/B/C/D くらいの粒度がわかりやすい。

```text
Aエリア: 入口または出口に近い
Bエリア: 中央寄り
Cエリア: 奥側
Dエリア: 比較的空きが出やすい
```

実際の意味はマップに合わせて後で正確に定義する。

### 管理者向けには細かい区分がほしい

警備員配置では A/B/C/D だけだと粗い可能性がある。

例:

```text
A入口
A奥
B中央通路
C出口側
D曲がり角
```

ただし MVP で最初から細かくしすぎると、Unity 側・backend 側・frontend 側の対応が重くなる。

おすすめ:

```text
MVP: areaId = A/B/C/D
拡張: zoneId = A入口 / A奥 / B中央通路 など
```

JSON は最初から `zoneId` を optional にしておく。

```json
{
  "areaId": "A",
  "zoneId": null
}
```

こうすると MVP は単純に始められて、管理者 AI の精度を上げたくなった時に細分化できる。

## ユーザー案内 API の形

MVP ではエリア推薦を返す。

```json
{
  "guidanceLevel": "area",
  "targetArea": {
    "areaId": "B",
    "label": "Bエリア",
    "reason": "Aエリアより混雑が低く、入口からの距離も短いため"
  },
  "reservation": {
    "status": "not_started",
    "reservationId": null
  },
  "route": {
    "svgPath": "M28 246 H264 V178 H342",
    "steps": [
      "西入口から中央通路へ進む",
      "Bエリア方面へ向かう",
      "空いている枠を確認しながら徐行"
    ]
  },
  "summary": {
    "emptyCount": 32,
    "congestionLevel": "normal"
  }
}
```

案内開始時:

```text
POST /api/guidance/start
```

request:

```json
{
  "userSessionId": "user_abc",
  "targetAreaId": "B"
}
```

response:

```json
{
  "reservationId": "res_001",
  "targetAreaId": "B",
  "expiresAt": "2026-06-28T14:40:00+09:00"
}
```

## 管理者 AI API の形

管理者がボタンを押した時だけ生成する。

```text
POST /api/admin/ai/recommendations
```

request:

```json
{
  "instruction": "混雑エリアを避けて入場車を誘導し、警備員配置を提案してください。"
}
```

backend が Gemini に渡す整形済みデータ:

```json
{
  "site": {
    "name": "第1駐車場",
    "timestamp": "2026-06-28T14:30:00+09:00"
  },
  "areaStatus": [
    {
      "areaId": "A",
      "emptyCount": 3,
      "occupiedCount": 57,
      "occupancyRate": 0.95,
      "waitingCars": 4,
      "riskLevel": "high"
    }
  ],
  "availableGuards": [
    {
      "guardId": "G01",
      "status": "active",
      "currentArea": "B",
      "canMove": true,
      "shiftEnd": "18:00",
      "breakStart": "13:00",
      "breakEnd": "14:00"
    }
  ],
  "alerts": [
    {
      "type": "congestion",
      "areaId": "A",
      "severity": "high"
    }
  ]
}
```

response:

```json
{
  "generatedAt": "2026-06-28T14:30:10+09:00",
  "source": "gemini",
  "title": "Aエリア混雑への警備配置提案",
  "summary": "Aエリアの混雑が高いため、入口側で分散案内を行い、B/Cエリアへ誘導してください。",
  "actions": [],
  "guardAssignments": [],
  "operatorNotes": []
}
```

fallback response:

```json
{
  "generatedAt": "2026-06-28T14:30:10+09:00",
  "source": "fallback",
  "title": "AI生成に失敗したため、ルールベース提案を表示",
  "summary": "混雑率が高いエリアを優先して警備員配置を検討してください。",
  "actions": [],
  "guardAssignments": [],
  "operatorNotes": [
    "Gemini API の応答を確認してください。"
  ]
}
```

## 管理者ログ

MVP では DB がなくても in-memory または JSON ファイルで始められる。

残すログ:

```json
{
  "id": "log_001",
  "timestamp": "2026-06-28T14:30:10+09:00",
  "type": "ai_generation_success",
  "message": "Gemini AI 提案を生成しました。",
  "metadata": {
    "model": "gemini-3.5-flash",
    "areas": ["A", "B"],
    "source": "manual"
  }
}
```

ログ type 案:

```text
unity_snapshot_received
unity_snapshot_stale
guidance_started
guidance_replanned
ai_generation_requested
ai_generation_success
ai_generation_failed
ai_fallback_used
admin_viewed_recommendation
```

## まだ考えるべきこと

### エリア予約 TTL は5分固定

MVP ではエリア予約 TTL を5分に固定する。

```text
案内開始 -> 5分間だけ targetAreaId を予約扱い
5分経過 -> 自動解除
```

5分にする理由:

- デモ中に予約が残り続ける問題を避けられる。
- 駐車場内の移動時間として短すぎない。
- 10分よりも回転が早く、複数ユーザー想定でも詰まりにくい。

## 残っている問題点

### Unity WebGL build が可能か

MVP は WebGL 推奨だが、Unity project が WebGL build で問題なく動くかは確認が必要。

確認項目:

- WebGL build が通るか。
- ブラウザで Play 映像が重すぎないか。
- Unity から backend へ HTTP POST できるか。
- CORS 設定が問題ないか。
- `postMessage` を使うか、backend へ直接送るか。

おすすめは backend へ直接 POST。

```text
Unity WebGL -> Backend -> Frontend
```

`postMessage` は短期デモ用として残す。

### WebGL build を最優先で確認する

MVP では WebGL iframe 方針を優先する。

最初にやること:

```text
Unity WebGL build を通す
```

失敗した場合:

- build error を確認する。
- 原因を特定する。
- Unity 側の設定、Package、Shader、Script、WebGL 非対応処理を修正する。
- 修正後に再度 WebGL build を試す。

この段階では Windows standalone へすぐ切り替えない。まず WebGL build 成功を目指す。

### WebGL から backend への通信

Unity WebGL はブラウザ上で動くため、backend へ HTTP POST する時に CORS の設定が必要。

確認すること:

- frontend の origin
- backend の origin
- Unity WebGL から `POST /api/unity/snapshot` が通るか
- localhost と本番デモ URL の違い

対策:

- backend 側で CORS allowlist を設定する。
- MVP では `localhost` と管理者画面の origin を許可する。
- API URL は Unity に直書きせず、設定値で変えられるようにする。

例:

```text
http://localhost:3000
http://localhost:5173
http://localhost:8000
管理者画面を配信する origin
```

Unity 側:

```text
Backend API URL は inspector / config / env 相当の設定値にする
```

### Unity WebGL の負荷

管理者画面に Unity 映像を表示すると、ブラウザ負荷が上がる。

確認すること:

- FPS が極端に落ちないか。
- 管理者画面の UI 操作が重くならないか。
- 1秒ごとの snapshot 送信で Play が詰まらないか。

対策:

- 現時点では大きな問題として扱わない。
- まず動かして確認する。
- 重かった場合に、Unity iframe の表示切替、描画品質、更新頻度、別方式を検討する。

### 座標変換の精度

Unity の world 座標と `parking.png` の向き・スケールが完全一致しているかは未確定。

問題:

- A/B/C/D polygon が画像上でズレる可能性がある。
- Unity の Z 軸と画像の Y 軸の向きが逆かもしれない。
- `parkingPoint` と見た目の枠中心が少し違う可能性がある。

対策:

- まず Unity slot 座標から自動生成する。
- 作ってから画像上に slot marker を一時表示してズレを確認する。
- ズレる場合は calibration point を追加して補正する。
- それでも見た目が合わない箇所は手動補正する。
- 最終的に area polygon へ padding と手動補正を入れられる構造にする。

### エリア予約と Unity 実状態のズレ

backend は Bエリアを予約扱いにしていても、Unity では Bエリアが混雑する可能性がある。

対策:

- Unity 実状態を優先する。
- `targetAreaId` の混雑率がしきい値を超えたら再案内する。
- 予約は5分で自動解除する。
- 再案内ログを残す。

### エリア案内の UX

個別枠を指定しないため、ユーザーがエリア到着後にどこへ止めるか迷う可能性がある。

対策:

- 画面文言を「Bエリア内の空き枠へ駐車してください」にする。
- エリアの空き台数を表示する。
- 将来は枠単位案内へ拡張できる JSON にしておく。

MVP 表示文言:

```text
Bエリアへ進み、空いている枠へ駐車してください。
```

余裕があれば表示する情報:

```text
Bエリア空き 12台
```

### Gemini の出力品質

Gemini が存在しない guardId / areaId を返す可能性がある。

対策:

- JSON schema validation を行う。
- backend で存在チェックする。
- 不正な `guardId` / `areaId` は破棄する。
- 壊れた JSON は fallback 提案を返す。
- AI提案は自動反映せず、表示だけにする。

### Gemini の呼び出し失敗

API key 未設定、レート制限、ネットワーク失敗、JSON parse 失敗がありえる。

対策:

- fallback report を返す。
- 管理者ログに `ai_generation_failed` と `ai_fallback_used` を残す。
- 前回の有効な提案があれば表示する。

### 管理者ログの保存先

MVP では in-memory でも動くが、ページ再読み込みや backend 再起動で消える。

選択肢:

- MVP: in-memory
- 少し堅くする: JSON file
- 本番寄り: SQLite / PostgreSQL

おすすめ:

```text
MVPは in-memory で開始。
時間があれば JSON file 保存を追加。
DB は後回し。
```

### 警備員データの個人情報

管理者デモには個人情報 JSON があるが、Gemini へ渡してはいけない。

対策:

- AI prompt に渡すのは `guardId`, status, currentArea, shift, break, canMove のみ。
- 氏名、住所、緊急連絡先、性別は渡さない。
- 管理者画面表示用データと AI 入力データを分ける。

### 発表デモ時の fallback 操作

発表中に Unity WebGL や Gemini が失敗すると見せ場が止まる。

現時点では発表時 fallback は優先度を下げる。

対策:

- backend に固定サンプル snapshot を用意する。
- Gemini 失敗時の fallback 提案を用意する。
- 管理者画面で「デモデータに切替」できるようにする。
- Unity iframe が動かなくても、マップ・ヒートマップ・AI提案は表示できるようにする。

### エリア polygon を誰が決めるか

MVP では Codex が Unity の slot 座標から初期 polygon を自動生成する。

ただし最終的には、画面上で見て違和感がないか調整が必要。

必要なら `docs` に calibration JSON と自動生成結果 JSON を作り、その後 backend/frontend 実装へ移す。

### AI にどこまで文章を書かせるか

MVP は管理者向け提案文だけ。

ユーザー向けの案内文は、まず backend のテンプレートで十分。

例:

```text
Bエリアへ案内します。Aエリアは混雑しているため、中央通路からBエリアへ進んでください。
```

ユーザー向け AI 文章は後回し。

## 追加決定事項

### 最小 snapshot exporter

最初に作る Unity exporter は最小構成にする。

目的:

```text
Unity WebGL -> POST /api/unity/snapshot -> backend
```

最小 payload:

```json
{
  "sourceId": "unity-webgl-admin-01",
  "scene": "SampleScene",
  "sequenceNumber": 1,
  "timestamp": "2026-06-28T05:30:00Z",
  "summary": {
    "empty": 0,
    "reserved": 0,
    "occupied": 0,
    "leaving": 0,
    "disabled": 0
  }
}
```

最初から全 slot / car を送らない。まず 1秒ごとに backend へ届くことだけ確認する。

### snapshot の順序管理

Unity が複数起動したり、リトライで古い snapshot が後から届く可能性がある。

MVP では snapshot に次を必ず入れる。

- `sourceId`
- `scene`
- `sequenceNumber`
- `timestamp`

backend は `sourceId + scene` ごとに最新の `sequenceNumber` を持つ。

古い snapshot は破棄する。

```text
incoming.sequenceNumber <= latest.sequenceNumber なら破棄
```

`timestamp` はログと stale 判定に使う。

### area master

Unity は `A/B/C/D`、frontend は `A区画` / `Bエリア` など表示名が変わる可能性がある。

MVP では backend に area master を持つ。

```json
{
  "areas": [
    { "areaId": "A", "label": "Aエリア", "displayOrder": 1 },
    { "areaId": "B", "label": "Bエリア", "displayOrder": 2 },
    { "areaId": "C", "label": "Cエリア", "displayOrder": 3 },
    { "areaId": "D", "label": "Dエリア", "displayOrder": 4 }
  ]
}
```

frontend は Unity の `areaId` を直接表示しない。backend が返す `label` を使う。

### 再案内しきい値

targetArea の混雑率で再案内を判定する。

```text
occupancyRate >= 0.85 -> 再案内候補
occupancyRate >= 0.95 -> 強制再案内
```

再案内の出しすぎを防ぐため、同じユーザーへの再案内 cooldown は30秒にする。

ただし満車時は即時再案内する。

### effectiveAvailable

物理的な空き台数だけでなく、案内中ユーザー数も推薦判定に入れる。

```text
effectiveAvailable = emptyCount - activeAreaReservations
```

`effectiveAvailable <= 0` のエリアは推薦しない。

### 予約期限切れ時の表示

5分経過後もユーザーが案内中の場合:

- frontend は「案内を更新」状態にする。
- backend は再推薦を返す。
- 古い予約は失効済みとして扱う。

表示例:

```text
案内情報を更新してください。
```

### ヒートマップ

MVP では Unity の heatmap texture を転送しない。

backend が計算した `area risk` を画像上に overlay する。

```text
low    -> 薄い青または緑
medium -> 黄
high   -> 赤
```

Unity heatmap texture の転送は後回し。

### Gemini 入力データ

Gemini には小さく整形した情報だけ渡す。

渡すもの:

- `areaSummary`
- `alerts`
- `availableGuards`
- `operatorInstruction`

渡さないもの:

- slot list 全件
- car list 全件
- 個人情報
- 全 snapshot

### prompt injection 対策

backend の system / developer prompt を固定する。

管理者の自由入力は system prompt に混ぜない。

```json
{
  "operatorInstruction": "混雑エリアを避けて警備員配置を提案してください"
}
```

禁止事項、出力 JSON schema、個人情報を使わないルールは常に backend 側 prompt で優先する。

### AI 結果 cache

同じ状態で AI ボタンを連打した時に、毎回 Gemini を呼ばない。

cache key:

```text
hash(areaStatus + alerts + availableGuards + operatorInstruction)
```

同じ hash の有効な AI 結果があれば前回結果を返す。

### ログの制限

ログに残さないもの:

- AI入力全文
- 警備員個人情報
- 全 snapshot
- 全 slot list
- 全 car list

ログに残すもの:

- イベント種別
- 発生時刻
- AI結果要約
- fallback 使用有無
- エラー内容の要約
- 対象 areaId

### frontend / framework

React / Next.js を使う。

Next.js は App Router を使う。

Unity 連携、Gemini、状態管理は FastAPI backend が担当する。

Next.js は画面と中継に寄せる。

サーバー処理は App Router の server route / server action 相当で FastAPI backend API を呼び、frontend へ返す。

MVP の考え方:

- ユーザー画面も管理者画面も React / Next.js で作る。
- 既存静的 HTML デモの見た目・文言・画像は参考にする。
- データ取得や Gemini 呼び出しは frontend 直ではなく server 側を通す。

役割分担:

```text
FastAPI backend:
  Unity snapshot ingest
  state store
  area risk score
  guidance reservation
  Gemini prompt / validation / fallback
  admin logs

Next.js:
  user UI
  admin UI
  API中継
  Unity iframe表示
```

### Unity WebGL build の置き場

Unity WebGL build は次に置く。

```text
WebApp/frontend/public/unity-build/
```

管理者画面の iframe src は固定で確認する。

```text
/unity-build/index.html
```

ただし巨大ファイルは Git 管理しない。

方針:

- Git には生成手順だけ docs に書く。
- デモ時だけ `public/unity-build/` に配置する。
- `.gitignore` で build 成果物を除外するか検討する。

### API key と時刻

Gemini API key は backend の `.env` のみに置く。

frontend と Unity には置かない。

時刻方針:

- backend 内部は UTC。
- JSON は ISO8601。
- 画面表示は Asia/Tokyo。

例:

```json
{
  "timestamp": "2026-06-28T05:30:00Z",
  "displayTimeZone": "Asia/Tokyo"
}
```

### セキュリティ

MVP ではまだ本格的なセキュリティは考えない。

ただし API key を frontend / Unity に置かないことだけは守る。

## さらに追加したMVP判断

### JSON file 保存

状態保存は MVP では in-memory で開始する。

余裕があれば JSON file 保存を追加する。

対象:

- admin logs
- last valid AI recommendation
- demo fallback snapshot

DB は後回し。

### 複数管理者画面

複数ブラウザで管理者画面を開いた場合の競合は MVP では許容する。

AI生成の重複は backend の cache / cooldown で軽減する。

### Unity sourceId

MVP の Unity sourceId は固定でよい。

```text
unity-webgl-admin-01
```

複数 Unity 起動への完全対応は後回し。

### stale と復帰

最終 snapshot 受信から5秒で stale とする。

```text
now - lastSnapshotReceivedAt >= 5秒 -> stale
```

Unity から snapshot が再度届いたら、自動で正常扱いに戻す。

### snapshot version

backend は snapshot version を持つ。

frontend へ返す状態には必ず次を含める。

```json
{
  "snapshotVersion": 123,
  "updatedAt": "2026-06-28T05:30:00Z"
}
```

画面には `updatedAt` を表示する。

### area polygon の歪み

Unity slot 座標から自動生成した area polygon が歪む可能性は MVP では許容する。

見た目が大きく崩れる場合だけ、padding / calibration point / 手動補正を入れる。

### risk score

エリアの危険度は単純な混雑率だけでなく、複数要素で決める。

MVP の入力:

- `capacity`
- `emptyCount`
- `occupancyRate`
- `waitingCars`
- `leavingCars`
- `staleVehicles`

簡易式:

```text
riskScore =
  occupancyRate * 100
  + waitingCars * 10
  + leavingCars * 5
  + staleVehicles * 15
```

例:

```text
riskScore < 70  -> low
70 <= riskScore < 90 -> medium
90 <= riskScore -> high
```

しきい値はデモで見ながら調整する。

### Gemini への警備員情報

Gemini には `availableGuards` だけ渡す。

警備員が不足している場合でも、AI には「追加配置が必要」と断定させない。

文言方針:

```text
現有2名で優先配置してください。
```

### AI UI文言

管理者画面では AI の結果を断定表示しない。

使う文言:

- 提案
- 参考
- 管理者判断

自動反映しない方針を維持する。

### 再案内理由

ユーザーに再案内する時は、短い理由を出す。

例:

```text
Bエリアが混雑したため、Cエリアへ案内を更新しました。
```

### guidance cancel API

案内キャンセル用 API を用意する。

```text
POST /api/guidance/cancel
```

MVP では実装優先度は高くない。

キャンセルされなくても、5分TTLで自然解除される。

### backend 内 reservation

MVP では backend のエリア予約を Unity へ戻さない。

予約は backend 内だけで扱う。

将来、必要になったら Unity へ area reservation summary を戻す。

### Gemini 生成中と timeout

管理者が AI ボタンを押したら、UI に生成中状態を出す。

```text
AI提案を生成中...
```

timeout したら fallback report を返す。

## 実装順の更新

- [x] backend に状態保存 API を作る。
- [x] 管理者画面へ固定 JSON を返して表示する。
- [x] Unity WebGL build を管理者画面 iframe に表示する。
- [x] Unity から backend へ snapshot を送る。
- [x] backend で A/B/C/D の混雑率を計算する。
- [x] ユーザー画面でエリア案内を表示する。
- [x] 案内開始ボタンでエリア予約を作る。
- [x] Unity の slot 座標から座標変換 JSON と area polygon を自動生成する。
- [x] 管理者画面で通常マップ / ヒートマップを切り替える。
- [x] 管理者 AI ボタンから Gemini を呼ぶ。
- [x] AI提案を表示し、ログに残す。
- [x] fallback を入れる。

### 実装メモ 2026-06-29

- FastAPI backend を `WebApp/backend` に追加した。
- `POST /api/unity/snapshot`、`GET /api/parking/status`、`GET /api/parking/recommendation`、`GET /api/admin/state`、`POST /api/guidance/start`、`POST /api/guidance/cancel`、`POST /api/admin/ai/recommendations` を実装した。
- backend は in-memory で snapshot version、stale 判定、area master、risk score、5分 TTL 予約、30秒 cooldown、AI cache、fallback report、管理者ログを扱う。
- Next.js App Router frontend を `WebApp/frontend` に追加し、ユーザー画面、管理者画面、backend proxy、Unity WebGL iframe、通常/ヒートマップ切替、AI生成 UI を実装した。
- Unity exporter として `UnityProject/Assets/Scripts/Network/UnityStateExporter.cs` を追加した。Unity Editor が再コンパイル後に `BackendBridge` へ `UnityStateExporter` を追加する必要がある。
- 2026-06-29 追記: Unity `2022.3.62f2` の `WebGLSupport` 導入を確認し、`C:\tmp\SmartParkingUnity2022Build_20260629144443` のコピー project から正式 WebGL build を実行した。出力先は `WebApp/frontend/public/unity-build/`。Next.js 経由で `/unity-build/index.html` が HTTP 200 を返すことと、`npm run build` 成功を確認した。

## 結論

MVP は次の形が一番現実的。

```text
案内粒度: 個別枠ではなく A/B/C/D エリア
予約: 案内開始ボタンでエリア予約
状態の正: Unity 優先
AI: 管理者が手動ボタンで生成
AIの役割: 説明文・提案文・警備員配置案
配置反映: しない。管理者判断
映像: 管理者画面に Unity WebGL iframe を表示
地図: Unity slot 座標から自動生成した area polygon を 637x492 の共通画像に overlay
ログ: 管理者画面に残す
```

これで、実装範囲を広げすぎず、発表で見せたい価値も出しやすい。
