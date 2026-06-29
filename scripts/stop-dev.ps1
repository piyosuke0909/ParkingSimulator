$ErrorActionPreference = "Continue"

$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$PidFiles = @(
    Join-Path $Root ".dev-backend.pid",
    Join-Path $Root ".dev-frontend.pid"
)

foreach ($PidFile in $PidFiles) {
    if (!(Test-Path $PidFile)) {
        continue
    }

    $ProcessId = Get-Content $PidFile | Select-Object -First 1
    if ($ProcessId) {
        $Process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        if ($Process) {
            Stop-Process -Id $ProcessId
            Write-Host "Stopped PID=$ProcessId"
        }
    }

    Remove-Item $PidFile -Force
}
