$ErrorActionPreference = "Stop"
if (-not (Test-Path ".env")) { Copy-Item ".env.example" ".env"; Write-Host ".env を作成しました。必要に応じて値を変更してから再実行してください。"; exit 0 }
docker compose up --build
