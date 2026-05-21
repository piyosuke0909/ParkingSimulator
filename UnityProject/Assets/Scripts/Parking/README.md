# Assets/Scripts/Parking/

駐車スペースの状態管理に関するスクリプト群。

## 主担当
**1人目**（スペース定義）・**4人目**（状態管理・統合）

## 想定するスクリプト

| ファイル名 | 内容 |
|-----------|------|
| `ParkingSpace.cs` | 1区画の空き・使用中の状態を持つ |
| `ParkingLotManager.cs` | 全スペースをまとめて管理する |
| `ParkingEventData.cs` | 駐車イベントのデータ構造定義 |

## データ構造の例
```csharp
public class ParkingEventData
{
    public string carId;
    public string spaceId;
    public string eventType; // "park" or "leave"
    public DateTime timestamp;
    public Vector3 position;
}
```
このクラスは `Vehicle/` と `Network/` の両方から参照される。
