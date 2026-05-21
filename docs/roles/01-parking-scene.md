# 1人目：駐車場シーン担当

駐車場の3D空間そのものを構築する役割。他のメンバーが使う「土台」を作る。

---

## 担当範囲

- 駐車場の3Dモデル・レイアウト
- 駐車スペースの定義・配置
- 照明・カメラ設定
- シーンファイルの管理

---

## 管理するフォルダ

```
UnityProject/Assets/
├── Scenes/
│   └── ParkingLot.unity    ← メインシーン（このファイルのオーナー）
├── Models/
│   ├── ParkingLot/         ← 駐車場の3Dモデル
│   └── Environment/        ← 地面・壁・柱など
├── Prefabs/
│   └── ParkingSpace.prefab ← 駐車スペース1区画のプレハブ
└── Materials/
    └── ParkingLot/         ← マテリアル・テクスチャ
```

---

## 他のメンバーとの連携ポイント

### 2人目（車・NPC）との連携
- 駐車スペースに **Collider** を必ずつける（車の停車判定に使う）
- 駐車スペースには **Tag** を設定する（例: `ParkingSpace`）
- 通路の幅は車が通れるサイズにする（車のサイズは2人目に確認）

### 3人目（データ・Web）との連携
- 駐車スペースに **ID** を振る仕組みが必要（スクリプトで管理）
  - 例：`ParkingSpace_01`, `ParkingSpace_02` ...
- スペースの座標情報を外部に渡せるようにする

### 4人目（統合）との連携
- シーン変更の前に `develop` ブランチの最新を取得する
- 大きなシーン変更前は Slack/Discord 等で一声かける

---

## ブランチ運用

```powershell
git checkout -b feature/parking-scene
# 作業
git push origin feature/parking-scene
# → 4人目にPRをレビュー依頼
```

---

## 作業の進め方（推奨）

1. まず駐車スペース1区画の Prefab を作る
2. Prefab を並べてシーンを構成する（直接置かない）
3. Collider・Tag を設定して2人目に渡す
4. モデルを追加するたびに `.meta` ファイルも一緒にコミット

---

## 注意事項

- `ParkingLot.unity` は基本的に **この担当だけが触る**
- 3Dモデル（.fbx）は Git LFS で管理されている（自動的に処理される）
- テクスチャ解像度は 2048x2048 以下を推奨（リポジトリ肥大化防止）
