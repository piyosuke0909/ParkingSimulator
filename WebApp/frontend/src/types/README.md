# frontend/src/types/

TypeScript の型定義ファイルを管理するフォルダ。  
バックエンドの Pydantic モデルと対応させる。

## 主担当
**3人目**

## 想定するファイル

| ファイル名 | 内容 |
|-----------|------|
| `parking.ts` | 駐車スペース・イベントの型 |
| `route.ts` | 経路情報の型 |

## 型定義例

```typescript
// parking.ts
export type SpaceStatus = "empty" | "occupied";

export interface ParkingSpace {
  spaceId: string;
  status: SpaceStatus;
  position: { x: number; y: number; z: number };
  carId?: string;
}
```

バックエンドの `models/parking.py` と**フィールド名・型を揃えること**。
