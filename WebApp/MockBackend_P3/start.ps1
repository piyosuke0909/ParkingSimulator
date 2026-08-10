Set-Location $PSScriptRoot
if (-not (Test-Path ".venv\Scripts\python.exe")) {
    py -m venv .venv
    & .\.venv\Scripts\python.exe -m pip install --upgrade pip
    & .\.venv\Scripts\python.exe -m pip install -r requirements.txt
}
$env:SMARTPARKING_LOCAL_API_KEY = "local-dev-key"
& .\.venv\Scripts\python.exe -m uvicorn main:app --host 127.0.0.1 --port 8000
