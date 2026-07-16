param(
    [switch]$NoInstall,
    [switch]$NoUnityRunner
)

$ErrorActionPreference = "Stop"

$ProcessPath = [Environment]::GetEnvironmentVariable("Path", "Process")
if (!$ProcessPath) {
    $ProcessPath = [Environment]::GetEnvironmentVariable("PATH", "Process")
}
if ($ProcessPath) {
    [Environment]::SetEnvironmentVariable("Path", $ProcessPath, "Process")
    [Environment]::SetEnvironmentVariable("PATH", $null, "Process")
}

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
$BackendErrorLog = Join-Path $LogDir "backend.err.log"
$FrontendLog = Join-Path $LogDir "frontend.log"
$FrontendErrorLog = Join-Path $LogDir "frontend.err.log"
$UnityRunnerLog = Join-Path $LogDir "unity-runner.log"
$BackendPidFile = Join-Path $Root ".dev-backend.pid"
$FrontendPidFile = Join-Path $Root ".dev-frontend.pid"
$UnityRunnerPidFile = Join-Path $Root ".dev-unity-runner.pid"
$UnityRunnerProfile = Join-Path $Root ".unity-runner-profile"

function Resolve-ExecutablePath {
    param(
        [string]$CommandName,
        [string[]]$FallbackPaths
    )

    $Command = Get-Command $CommandName -ErrorAction SilentlyContinue
    if ($Command) {
        return $Command.Source
    }

    foreach ($FallbackPath in $FallbackPaths) {
        if ($FallbackPath -and (Test-Path $FallbackPath)) {
            return $FallbackPath
        }
    }

    throw "Required command was not found: $CommandName"
}

$PythonFallbacks = @()
if ($env:LOCALAPPDATA) {
    $PythonFallbacks += Join-Path $env:LOCALAPPDATA "Programs\Python\Python313\python.exe"
    $PythonRoot = Join-Path $env:LOCALAPPDATA "Programs\Python"
    if (Test-Path $PythonRoot) {
        $PythonFallbacks += Get-ChildItem -Path $PythonRoot -Recurse -Filter python.exe -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }
    }
}

$NpmFallbacks = @(
    (Join-Path $env:ProgramFiles "nodejs\npm.cmd"),
    (Join-Path ${env:ProgramFiles(x86)} "nodejs\npm.cmd")
)
$NodeFallbacks = @(
    (Join-Path $env:ProgramFiles "nodejs\node.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "nodejs\node.exe")
)

$PythonPath = Resolve-ExecutablePath -CommandName "python" -FallbackPaths $PythonFallbacks
$NpmPath = Resolve-ExecutablePath -CommandName "npm.cmd" -FallbackPaths $NpmFallbacks
$NodePath = Resolve-ExecutablePath -CommandName "node" -FallbackPaths $NodeFallbacks
$NextBin = Join-Path $FrontendDir "node_modules\next\dist\bin\next"

function Start-DevProcess {
    param(
        [string]$Name,
        [string]$WorkingDirectory,
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$StandardOutput,
        [string]$StandardError,
        [string]$PidFile
    )

    $Process = Start-Process `
        -FilePath $FilePath `
        -ArgumentList $ArgumentList `
        -WorkingDirectory $WorkingDirectory `
        -WindowStyle Hidden `
        -RedirectStandardOutput $StandardOutput `
        -RedirectStandardError $StandardError `
        -PassThru

    Set-Content -Path $PidFile -Value $Process.Id -Encoding ASCII
    Write-Host "$Name started. PID=$($Process.Id)"
}

function Wait-ForUrl {
    param(
        [string]$Url,
        [int]$TimeoutSeconds = 30
    )

    $Deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $Deadline) {
        try {
            $Response = Invoke-WebRequest -UseBasicParsing -Uri $Url -Method Head -TimeoutSec 2
            if ($Response.StatusCode -ge 200 -and $Response.StatusCode -lt 500) {
                return $true
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    return $false
}

function Get-BrowserPath {
    $Candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Microsoft\Edge\Application\msedge.exe"),
        (Join-Path $env:ProgramFiles "Microsoft\Edge\Application\msedge.exe"),
        (Join-Path $env:ProgramFiles "Google\Chrome\Application\chrome.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Google\Chrome\Application\chrome.exe")
    )

    foreach ($Candidate in $Candidates) {
        if (Test-Path $Candidate) {
            return $Candidate
        }
    }

    return $null
}

function Start-UnityWebGLRunner {
    if ($NoUnityRunner) {
        Write-Host "Unity WebGL runner skipped."
        return
    }

    $BrowserPath = Get-BrowserPath
    if (!$BrowserPath) {
        Write-Warning "Unity WebGL runner was not started because Edge/Chrome was not found."
        return
    }

    New-Item -ItemType Directory -Force -Path $UnityRunnerProfile | Out-Null

    $UnityUrl = "http://127.0.0.1:3000/unity-build/index.html?runner=dev"
    if (!(Wait-ForUrl -Url $UnityUrl -TimeoutSeconds 90)) {
        Write-Warning "Unity WebGL runner was not started because frontend did not serve $UnityUrl in time."
        return
    }

    $Arguments = @(
        "--headless=new",
        "--disable-gpu",
        "--disable-background-timer-throttling",
        "--disable-renderer-backgrounding",
        "--disable-backgrounding-occluded-windows",
        "--autoplay-policy=no-user-gesture-required",
        "--enable-webgl",
        "--use-gl=angle",
        "--use-angle=swiftshader",
        "--user-data-dir=$UnityRunnerProfile",
        "--log-file=$UnityRunnerLog",
        $UnityUrl
    )

    $Process = Start-Process `
        -FilePath $BrowserPath `
        -ArgumentList $Arguments `
        -WindowStyle Hidden `
        -PassThru

    Set-Content -Path $UnityRunnerPidFile -Value $Process.Id -Encoding ASCII
    Write-Host "Unity WebGL runner started. PID=$($Process.Id)"
}

Start-DevProcess `
    -Name "backend" `
    -WorkingDirectory $BackendDir `
    -FilePath $PythonPath `
    -ArgumentList @("-m", "uvicorn", "main:app", "--host", "127.0.0.1", "--port", "8000") `
    -StandardOutput $BackendLog `
    -StandardError $BackendErrorLog `
    -PidFile $BackendPidFile

Start-DevProcess `
    -Name "frontend" `
    -WorkingDirectory $FrontendDir `
    -FilePath $NodePath `
    -ArgumentList @($NextBin, "dev", "--hostname", "127.0.0.1", "--port", "3000") `
    -StandardOutput $FrontendLog `
    -StandardError $FrontendErrorLog `
    -PidFile $FrontendPidFile
Start-UnityWebGLRunner

Start-Sleep -Seconds 3

Write-Host ""
Write-Host "SmartParking dev servers are starting."
Write-Host "User:  http://127.0.0.1:3000"
Write-Host "Admin: http://127.0.0.1:3000/admin"
Write-Host "API:   http://127.0.0.1:8000/api/health"
Write-Host ""
Write-Host "Logs:"
Write-Host "  $BackendLog"
Write-Host "  $BackendErrorLog"
Write-Host "  $FrontendLog"
Write-Host "  $FrontendErrorLog"
Write-Host "  $UnityRunnerLog"
Write-Host ""
Write-Host "Stop:"
Write-Host "  powershell -ExecutionPolicy Bypass -File .\scripts\stop-dev.ps1"
