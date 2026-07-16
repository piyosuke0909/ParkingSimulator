$ErrorActionPreference = "Continue"

$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$PidFiles = @(
    (Join-Path $Root ".dev-backend.pid"),
    (Join-Path $Root ".dev-frontend.pid"),
    (Join-Path $Root ".dev-unity-runner.pid")
)

function Stop-ProcessTree {
    param(
        [int]$ProcessId
    )

    $Children = Get-CimInstance Win32_Process -Filter "ParentProcessId = $ProcessId" -ErrorAction SilentlyContinue
    foreach ($Child in $Children) {
        Stop-ProcessTree -ProcessId $Child.ProcessId
    }

    $Process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($Process) {
        Stop-Process -Id $ProcessId -Force
        Write-Host "Stopped PID=$ProcessId"
    }
}

foreach ($PidFile in $PidFiles) {
    if (!(Test-Path $PidFile)) {
        continue
    }

    $ProcessId = Get-Content $PidFile | Select-Object -First 1
    if ($ProcessId) {
        Stop-ProcessTree -ProcessId ([int]$ProcessId)
    }

    Remove-Item $PidFile -Force
}

foreach ($Port in @(3000, 8000)) {
    $Connections = netstat -ano | Select-String "127\.0\.0\.1:$Port\s+.*LISTENING"
    foreach ($Connection in $Connections) {
        $Parts = ($Connection.Line -split "\s+") | Where-Object { $_ }
        $ProcessId = [int]$Parts[-1]
        Stop-ProcessTree -ProcessId $ProcessId
    }
}
