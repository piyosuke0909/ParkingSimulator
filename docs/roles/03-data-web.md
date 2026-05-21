# 3人目：データ・Web担当

Unityから得たデータを受け取り、最適経路の計算・Web上での可視化を担当。

---

## 担当範囲

- Python バックエンド（FastAPI）の構築
- Unity → API → DB のデータパイプライン
- 駐車場の最適経路・空きスペース計算
- React フロントエンドの構築
- 駐車状況のリアルタイム表示

---

## 管理するフォルダ

```
WebApp/
├── backend/
│   ├── main.py                  ← FastAPI エントリーポイント
│   ├── routers/
│   │   ├── parking.py           ← 駐車情報API
│   │   └── route.py             ← 最適経路API
│   ├── models/
│   │   └── parking.py           ← データモデル
│   ├── services/
│   │   └── route_optimizer.py   ← 最適経路計算ロジック
│   └── requirements.txt
│
└── frontend/
    ├── src/
    │   ├── components/
    │   │   ├── ParkingMap.tsx    ← 駐車場マップ表示
    │   │   └── CarStatus.tsx    ← 車の状態表示
    │   └── App.tsx
    └── package.json

DataAnalysis/
├── scripts/
│   ├── analyze_parking.py       ← 駐車データ分析
│   └── optimize_route.py        ← 経路最適化
└── data/
    └── samples/                 ← テスト用サンプルデータ
```

---

## API 仕様（Unityとの連携インターフェース）

Unity（2人目）からのデータ受信：

```
POST /api/parking/event
Content-Type: application/json

{
  "carId": "car_001",
  "spaceId": "space_A1",
  "eventType": "park",       // "park" or "leave"
  "timestamp": "2025-01-01T12:00:00",
  "position": { "x": 10.5, "y": 0, "z": 3.2 }
}
```

空き状況の取得（React フロントから呼ぶ）：

```
GET /api/parking/status
→ 全スペースの空き・使用中一覧を返す

GET /api/route/optimal?from=entrance&to=nearest_space
→ 最適経路を座標リストで返す
```

---

## 他のメンバーとの連携ポイント

### 2人目（車・NPC）との連携
- `POST /api/parking/event` のリクエスト仕様を2人目と合わせる
- ローカルで `uvicorn main:app --reload` で起動してテストしてもらう

### 1人目（シーン）との連携
- 駐車スペースの ID と座標マップが必要
  - 1人目から `spaceId → position` の対応表をもらう

### 4人目（統合）との連携
- API の URL は環境変数で切り替えられるようにする
  - 開発中：`http://localhost:8000`
  - 本番：別途決定

---

## ブランチ運用

```powershell
git checkout -b feature/data-web
# 作業
git push origin feature/data-web
```

---

## ローカル起動手順

```powershell
# バックエンド
cd WebApp/backend
pip install -r requirements.txt
uvicorn main:app --reload
# → http://localhost:8000/docs でAPI確認できる

# フロントエンド
cd WebApp/frontend
npm install
npm run dev
# → http://localhost:3000 で確認
```

---

## 最適経路の考え方（案）

- 現在地（入口）から最寄りの空きスペースまでの経路
- アルゴリズム候補：Dijkstra・A*・BFS
- 駐車場のレイアウトをグラフ（ノード＋エッジ）として表現
- 1人目のシーン情報からグラフを生成する

---

## 注意事項

- Unity が送るデータ量は多くなりうる（1秒に1回など）のでキューで受け取る設計にする
- フロントは Unity のシーンと同じ座標系でマップを描くと分かりやすい
- CORS 設定を忘れずに（Unity → FastAPI への通信に必要）
