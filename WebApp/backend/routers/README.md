# WebApp/backend/routers/

FastAPI のルーター（API エンドポイント）を管理するフォルダ。  
機能ごとにファイルを分けて管理する。

## 主担当
**3人目**

## 想定するファイル

| ファイル名 | 内容 |
|-----------|------|
| `parking.py` | 駐車イベント受信・空き状況返却 |
| `route.py` | 最適経路の計算・返却 |

## Unity との連携仕様

```python
# parking.py のエンドポイント例
@router.post("/api/parking/event")
async def receive_parking_event(event: ParkingEventData):
    # Unity から受け取るデータ
    # {
    #   "carId": "car_001",
    #   "spaceId": "A-01",
    #   "eventType": "park",  # or "leave"
    #   "timestamp": "2025-01-01T12:00:00",
    #   "position": {"x": 10.5, "y": 0, "z": 3.2}
    # }
```

データ構造の詳細は `../models/README.md` を参照。
