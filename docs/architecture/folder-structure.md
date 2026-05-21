# フォルダ構成

---

## リポジトリ全体

```
ParkingSimulator/
├── UnityProject/          ← Unityプロジェクト本体
├── WebApp/                ← Web アプリ（フロント＋バック）
├── DataAnalysis/          ← データ解析スクリプト
├── docs/                  ← ドキュメント（このフォルダ）
├── .gitignore
└── .gitattributes         ← Git LFS の設定
```

---

## UnityProject/Assets/

```
Assets/
├── Scenes/
│   ├── Main.unity             ← 統合シーン（4人目管理）
│   ├── ParkingLot.unity       ← 駐車場シーン（1人目管理）
│   └── Vehicles.unity         ← 車シーン（2人目管理）
│
├── Scripts/
│   ├── Vehicle/               ← 車・NPC 関連（2人目）
│   ├── Parking/               ← 駐車スペース管理（1・4人目）
│   ├── Network/               ← API 通信（4人目）
│   └── UI/                    ← UI 制御（4人目）
│
├── Prefabs/
│   ├── Cars/                  ← 車プレハブ（2人目）
│   └── Parking/               ← 駐車スペースプレハブ（1人目）
│
├── Models/
│   ├── Cars/                  ← 車3Dモデル（2人目）
│   ├── ParkingLot/            ← 駐車場3Dモデル（1人目）
│   └── Environment/           ← 環境モデル（1人目）
│
├── Materials/                 ← マテリアル・テクスチャ（各担当）
├── Audio/                     ← 音声ファイル
└── UI/                        ← UI アセット
```

---

## WebApp/

```
WebApp/
├── backend/
│   ├── main.py                ← FastAPI アプリ起動
│   ├── routers/               ← API ルーター
│   ├── models/                ← データモデル（Pydantic）
│   ├── services/              ← ビジネスロジック（経路計算など）
│   ├── database.py            ← DB 接続
│   └── requirements.txt
│
└── frontend/
    ├── src/
    │   ├── components/        ← React コンポーネント
    │   ├── pages/             ← ページ単位
    │   ├── hooks/             ← カスタムフック
    │   └── types/             ← TypeScript 型定義
    ├── package.json
    └── tsconfig.json
```

---

## DataAnalysis/

```
DataAnalysis/
├── scripts/
│   ├── analyze_parking.py     ← 駐車データの集計・分析
│   └── optimize_route.py      ← 最適経路計算
├── notebooks/                 ← Jupyter Notebook（探索的分析）
├── data/
│   ├── samples/               ← テスト用サンプルデータ
│   └── .gitkeep               ← 実データはコミットしない
└── requirements.txt
```

---

## ファイルの所有者まとめ

| ファイル・フォルダ | 主担当 | 他の人が触ってもいいか |
|-------------------|--------|----------------------|
| `Assets/Scenes/ParkingLot.unity` | 1人目 | 変更前に相談 |
| `Assets/Scenes/Vehicles.unity` | 2人目 | 変更前に相談 |
| `Assets/Scenes/Main.unity` | 4人目 | 変更前に相談 |
| `Assets/Scripts/Vehicle/` | 2人目 | PR 必須 |
| `Assets/Scripts/Network/` | 4人目 | PR 必須 |
| `WebApp/` | 3人目 | PR 必須 |
| `DataAnalysis/` | 3人目 | 自由 |
| `docs/` | 4人目 | 全員が更新可 |
