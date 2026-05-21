# DataAnalysis/data/samples/

開発・テスト用のサンプルデータを置くフォルダ。

## 主担当
**3人目**

## 想定するファイル

| ファイル名 | 内容 |
|-----------|------|
| `parking_events.json` | 駐車・出庫イベントのサンプル |
| `space_layout.json` | 駐車スペースの ID・座標マップ |

## space_layout.json の形式例
1人目が作った駐車場シーンの座標を JSON で書き出したもの。  
経路計算のグラフ構築に使う。

```json
{
  "spaces": [
    { "spaceId": "A-01", "position": { "x": 5.0, "y": 0, "z": 3.0 } },
    { "spaceId": "A-02", "position": { "x": 5.0, "y": 0, "z": 6.0 } }
  ],
  "entrances": [
    { "id": "entrance_main", "position": { "x": 0, "y": 0, "z": 0 } }
  ]
}
```
