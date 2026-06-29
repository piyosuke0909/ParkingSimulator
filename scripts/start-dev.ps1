param(
    [switch]$NoInstall
)

$ErrorActionPreference = "Stop"

$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$BackendDir = Join-Path $Root "WebApp\backend"
$FrontendDir = Join-Path $Root "WebApp\frontend"
$LogDir = Join-Path $Root ".logs"

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

$BackendEnv = Join-Path $BackendDir ".env"
$BackendEnvExample = Join-Path $BackendDir ".env.example"
$FrontendEnv = Join-Path $FrontendDir ".env"
$FrontendEnvExample = Join-Path $FrontendDir ".env.example"

if (!(Test-Path $BackendEnv) -and (Test-Path $BackendEnvExample)) {
    Copy-Item $BackendEnvExample $BackendEnv
    Write-Host "Created WebApp\backend\.env from .env.example"
}

if (!(Test-Path $FrontendEnv) -and (Test-Path $FrontendEnvExample)) {
    Copy-Item $FrontendEnvExample $FrontendEnv
    Write-Host "Created WebApp\frontend\.env from .env.example"
}

if (!$NoInstall -and !(Test-Path (Join-Path $FrontendDir "node_modules"))) {
    Write-Host "Installing frontend dependencies..."
    Push-Location $FrontendDir
    npm.cmd install
    Pop-Location
}

$BackendLog = Join-Path $LogDir "backend.log"
$FrontendLog = Join-Path $LogDir "frontend.log"
$BackendPidFile = Join-Path $Root ".dev-backend.pid"
$FrontendPidFile = Join-Path $Root ".dev-frontend.pid"

function Start-DevProcess {
    param(
        [string]$Name,
        [string]$WorkingDirectory,
        [string]$Command,
        [string]$PidFile
    )

    $Process = Start-Process `
        -FilePath "powershell.exe" `
        -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $Command) `
        -WorkingDirectory $WorkingDirectory `
        -WindowStyle Hidden `
        -PassThru

    Set-Content -Path $PidFile -Value $Process.Id -Encoding ASCII
    Write-Host "$Name started. PID=$($Process.Id)"
}

$BackendCommand = "Set-Location -LiteralPath '$BackendDir'; python -m uvicorn main:app --host 127.0.0.1 --port 8000 *> '$BackendLog'"
$FrontendCommand = "Set-Location -LiteralPath '$FrontendDir'; npm.cmd run dev -- --hostname 127.0.0.1 --port 3000 *> '$FrontendLog'"

Start-DevProcess -Name "backend" -WorkingDirectory $BackendDir -Command $BackendCommand -PidFile $BackendPidFile
Start-DevProcess -Name "frontend" -WorkingDirectory $FrontendDir -Command $FrontendCommand -PidFile $FrontendPidFile

Start-Sleep -Seconds 3

Write-Host ""
Write-Host "SmartParking dev servers are starting."
Write-Host "User:  http://127.0.0.1:3000"
Write-Host "Admin: http://127.0.0.1:3000/admin"
Write-Host "API:   http://127.0.0.1:8000/api/health"
Write-Host ""
Write-Host "Logs:"
Write-Host "  $BackendLog"
Write-Host "  $FrontendLog"
Write-Host ""
Write-Host "Stop:"
Write-Host "  powershell -ExecutionPolicy Bypass -File .\scripts\stop-dev.ps1"
