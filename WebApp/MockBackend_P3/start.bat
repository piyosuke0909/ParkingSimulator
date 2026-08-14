@echo off
cd /d %~dp0
where py >nul 2>nul
if %errorlevel%==0 (
  set PY=py
) else (
  set PY=python
)
if not exist .venv\Scripts\python.exe (
  %PY% -m venv .venv
  .venv\Scripts\python.exe -m pip install --upgrade pip
  .venv\Scripts\python.exe -m pip install -r requirements.txt
)
set SMARTPARKING_LOCAL_API_KEY=local-dev-key
.venv\Scripts\python.exe -m uvicorn main:app --host 127.0.0.1 --port 8000
pause
