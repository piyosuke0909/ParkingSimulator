# WebApp/

駐車状況をブラウザで表示・管理するための Web アプリ。  
Unity から受け取ったデータを可視化し、最適経路を提示する。

## 主担当
**3人目**

## 構成

| フォルダ | 内容 |
|---------|------|
| `backend/` | Python（FastAPI）サーバー |
| `frontend/` | React フロントエンド |

## システム図
```
Unity（2人目のスクリプト）
    ↓ POST /api/parking/event
backend/（FastAPI）
    ↓ データ処理・経路計算
frontend/（React）
    ↓ ブラウザで表示
ユーザー（どこに停めたか確認）
```

## ローカル起動
```powershell
# バックエンド（ポート8000）
cd backend
pip install -r requirements.txt
uvicorn main:app --reload

# フロントエンド（ポート3000）
cd frontend
npm install
npm run dev
```
