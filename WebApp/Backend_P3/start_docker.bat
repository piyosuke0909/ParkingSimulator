@echo off
if not exist .env (
  copy .env.example .env >nul
  echo .env を作成しました。必要に応じて値を変更してから再実行してください。
  pause
  exit /b 0
)
docker compose up --build
