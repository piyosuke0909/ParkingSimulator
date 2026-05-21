# WebApp/backend/models/

Pydantic によるデータモデル（型定義）を管理するフォルダ。  
API のリクエスト・レスポンスの形を定義する。

## 主担当
**3人目**（定義）・**4人目**（Unity 側の対応する構造と合わせる）

## 想定するファイル

| ファイル名 | 内容 |
|-----------|------|
| `parking.py` | 駐車イベント・スペース状態のモデル |
| `route.py` | 経路情報のモデル |

## データ構造例

```python
# parking.py
from pydantic import BaseModel
from datetime import datetime

class Position(BaseModel):
    x: float
    y: float
    z: float

class ParkingEventData(BaseModel):
    carId: str
    spaceId: str
    eventType: str   # "park" or "leave"
    timestamp: datetime
    position: Position
```

## Unity 側の対応
Unity（C#）側で同じフィールドを持つクラスを `Scripts/Parking/ParkingEventData.cs` に定義する。  
フィールド名・型を必ず合わせること。
