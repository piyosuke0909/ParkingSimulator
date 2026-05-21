# frontend/src/hooks/

API 通信・データ取得のカスタムフックを管理するフォルダ。  
コンポーネントから API 呼び出しロジックを分離する。

## 主担当
**3人目**

## 想定するフック

| ファイル名 | 内容 |
|-----------|------|
| `useParkingStatus.ts` | 全スペースの空き状況をポーリングで取得 |
| `useOptimalRoute.ts` | 最適経路を API から取得 |

## 使い方イメージ

```tsx
// ParkingMap.tsx の中で使う
const { spaces, loading } = useParkingStatus();
```

API の URL（`http://localhost:8000`）はここで環境変数から読み込む。
