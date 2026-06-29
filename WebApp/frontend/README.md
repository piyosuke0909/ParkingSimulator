# WebApp/frontend

SmartParking MVP の Next.js frontend です。ユーザー画面、管理者画面、FastAPI backend への proxy、Unity WebGL iframe 表示を担当します。

## 必要環境

- Node.js / npm
- Next.js 16
- React 19
- TypeScript

## セットアップ

```powershell
cd WebApp\frontend
npm install
Copy-Item .env.example .env
```

`.env`:

```env
BACKEND_URL=http://localhost:8000
```

## 起動

```powershell
npm run dev -- --hostname 127.0.0.1 --port 3000
```

確認:

```text
ユーザー画面: http://127.0.0.1:3000
管理者画面:   http://127.0.0.1:3000/admin
Unity WebGL:  http://127.0.0.1:3000/unity-build/index.html
```

## 画面

| Path | 内容 |
| --- | --- |
| `/` | ユーザー向け駐車エリア案内 |
| `/admin` | 管理者向け Unity Play / 混雑状況 / AI 提案 |
| `/api/backend/*` | FastAPI backend への proxy |

## Unity WebGL build

管理者画面は次の path を iframe で表示します。

```text
/unity-build/index.html
```

実ファイルの配置先:

```text
WebApp/frontend/public/unity-build/
```

期待される構成:

```text
public/unity-build/
  index.html
  Build/
  TemplateData/
```

MVP ではチーム共有のため、この WebGL build 成果物も Git に含めます。将来 100MB を超えるファイルが出た場合は Git LFS または GitHub Releases に移してください。

## build 確認

```powershell
npm run build
```
