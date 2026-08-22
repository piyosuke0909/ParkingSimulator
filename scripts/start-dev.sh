#!/usr/bin/env bash
set -euo pipefail

NO_INSTALL=0
NO_UNITY_RUNNER=0

usage() {
  cat <<'EOF'
Usage: ./scripts/start-dev.sh [--no-install] [--no-unity-runner]

  --no-install        Skip dependency installation.
  --no-unity-runner   Do not start the headless Unity WebGL browser runner.
EOF
}

for arg in "$@"; do
  case "$arg" in
    --no-install) NO_INSTALL=1 ;;
    --no-unity-runner) NO_UNITY_RUNNER=1 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $arg" >&2; usage >&2; exit 1 ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
BACKEND_DIR="$ROOT/WebApp/backend"
FRONTEND_DIR="$ROOT/WebApp/frontend"
LOG_DIR="$ROOT/.logs"
UNITY_INDEX_PATH="$FRONTEND_DIR/public/unity-build/index.html"

mkdir -p "$LOG_DIR"

is_supported_python() {
  "$1" -c 'import sys; raise SystemExit(0 if sys.version_info >= (3, 11) else 1)' >/dev/null 2>&1
}

find_python() {
  local candidate
  for candidate in \
    python3.13 \
    python3.12 \
    python3.11 \
    /opt/homebrew/opt/python@3.13/bin/python3.13 \
    /opt/homebrew/opt/python@3.12/bin/python3.12 \
    /opt/homebrew/opt/python@3.11/bin/python3.11 \
    /usr/local/opt/python@3.13/bin/python3.13 \
    /usr/local/opt/python@3.12/bin/python3.12 \
    /usr/local/opt/python@3.11/bin/python3.11; do
    if command -v "$candidate" >/dev/null 2>&1 && is_supported_python "$candidate"; then
      command -v "$candidate"
      return 0
    fi
  done
  return 1
}

copy_env_if_needed() {
  local dir="$1"
  if [[ ! -f "$dir/.env" && -f "$dir/.env.example" ]]; then
    cp "$dir/.env.example" "$dir/.env"
    echo "Created ${dir#$ROOT/}/.env from .env.example"
  fi
}

is_running() {
  [[ -n "${1:-}" ]] && kill -0 "$1" 2>/dev/null
}

prepare_pid_file() {
  local name="$1"
  local pid_file="$2"

  if [[ ! -f "$pid_file" ]]; then
    return 0
  fi

  local pid
  pid="$(head -n 1 "$pid_file")"
  if is_running "$pid"; then
    echo "$name is already running (PID=$pid). Run ./scripts/stop-dev.sh first." >&2
    exit 1
  fi
  rm -f "$pid_file"
}

ensure_port_available() {
  local port="$1"
  local pid
  pid="$(lsof -tiTCP:"$port" -sTCP:LISTEN 2>/dev/null | head -n 1 || true)"
  if [[ -n "$pid" ]]; then
    echo "Port $port is already in use (PID=$pid). Stop that process or run ./scripts/stop-dev.sh." >&2
    exit 1
  fi
}

wait_for_url() {
  local url="$1"
  local attempts="${2:-60}"
  local api_key="${3:-}"

  for ((i = 0; i < attempts; i += 1)); do
    if [[ -n "$api_key" ]] && curl --fail --silent --show-error -H "X-API-Key: $api_key" "$url" >/dev/null 2>&1; then
      return 0
    fi
    if [[ -z "$api_key" ]] && curl --fail --silent --show-error "$url" >/dev/null 2>&1; then
      return 0
    fi
    sleep 0.5
  done
  return 1
}

start_process() {
  local name="$1"
  local working_dir="$2"
  local stdout_log="$3"
  local stderr_log="$4"
  local pid_file="$5"
  shift 5

  (
    cd "$working_dir"
    nohup "$@" >"$stdout_log" 2>"$stderr_log" < /dev/null &
    echo $! > "$pid_file"
  )
  echo "$name started. PID=$(cat "$pid_file")"
}

find_browser() {
  local candidate
  for candidate in \
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" \
    "/Applications/Chromium.app/Contents/MacOS/Chromium" \
    "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"; do
    if [[ -x "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  for candidate in google-chrome chromium chromium-browser microsoft-edge; do
    if command -v "$candidate" >/dev/null 2>&1; then
      command -v "$candidate"
      return 0
    fi
  done
  return 1
}

start_unity_runner() {
  if [[ "$NO_UNITY_RUNNER" -eq 1 ]]; then
    echo "Unity WebGL runner skipped."
    return
  fi

  if [[ ! -f "$UNITY_INDEX_PATH" ]] || ! grep -q "smartParkingBackendMode" "$UNITY_INDEX_PATH"; then
    echo "Unity WebGL runner was not started because the committed build is older than the viewer/sender split. Rebuild with SmartParkingWebGLBuild.Build first." >&2
    return
  fi

  local browser
  if ! browser="$(find_browser)"; then
    echo "Unity WebGL runner was not started because Chrome, Chromium, or Edge was not found." >&2
    return
  fi

  local api_key="${SMARTPARKING_LOCAL_API_KEY:-local-dev-key}"
  local encoded_api_key
  encoded_api_key="$("$PYTHON_BIN" -c 'import sys, urllib.parse; print(urllib.parse.quote(sys.argv[1], safe=""))' "$api_key")"
  local unity_url="http://127.0.0.1:3000/unity-build/index.html?runner=dev&backendMode=sender&apiKey=$encoded_api_key"
  if ! wait_for_url "$unity_url" 180; then
    echo "Unity WebGL runner was not started because the frontend did not serve the Unity build in time." >&2
    return
  fi

  mkdir -p "$UNITY_RUNNER_PROFILE"
  nohup "$browser" \
    --headless=new \
    --disable-gpu \
    --disable-background-timer-throttling \
    --disable-renderer-backgrounding \
    --disable-backgrounding-occluded-windows \
    --autoplay-policy=no-user-gesture-required \
    --enable-webgl \
    --use-angle=swiftshader \
    --user-data-dir="$UNITY_RUNNER_PROFILE" \
    --log-file="$UNITY_RUNNER_LOG" \
    "$unity_url" > /dev/null 2>&1 < /dev/null &
  echo $! > "$UNITY_RUNNER_PID_FILE"
  echo "Unity WebGL runner started. PID=$(cat "$UNITY_RUNNER_PID_FILE")"
}

copy_env_if_needed "$BACKEND_DIR"
copy_env_if_needed "$FRONTEND_DIR"

CONFIGURED_LOCAL_API_KEY="${SMARTPARKING_LOCAL_API_KEY:-local-dev-key}"
if [[ -z "${SMARTPARKING_LOCAL_API_KEY:-}" && -f "$BACKEND_DIR/.env" ]]; then
  while IFS='=' read -r key value; do
    if [[ "$key" == "SMARTPARKING_LOCAL_API_KEY" && -n "$value" ]]; then
      CONFIGURED_LOCAL_API_KEY="${value%$'\r'}"
      CONFIGURED_LOCAL_API_KEY="${CONFIGURED_LOCAL_API_KEY%\"}"
      CONFIGURED_LOCAL_API_KEY="${CONFIGURED_LOCAL_API_KEY#\"}"
      break
    fi
  done < "$BACKEND_DIR/.env"
fi
export SMARTPARKING_LOCAL_API_KEY="$CONFIGURED_LOCAL_API_KEY"

if ! PYTHON_SOURCE="$(find_python)"; then
  echo "Python 3.11 or later is required. On macOS with Homebrew, run: brew install python@3.13" >&2
  exit 1
fi

if [[ -x "$BACKEND_DIR/.venv/bin/python" ]] && ! is_supported_python "$BACKEND_DIR/.venv/bin/python"; then
  echo "The existing backend virtual environment uses an unsupported Python version." >&2
  echo "After installing Python 3.11 or later, remove $BACKEND_DIR/.venv and run this script again." >&2
  exit 1
fi

if [[ "$NO_INSTALL" -eq 0 ]]; then
  if [[ -f "$BACKEND_DIR/requirements.txt" && ! -x "$BACKEND_DIR/.venv/bin/python" ]]; then
    echo "Creating backend virtual environment..."
    "$PYTHON_SOURCE" -m venv "$BACKEND_DIR/.venv"
  fi

  if [[ -x "$BACKEND_DIR/.venv/bin/python" ]]; then
    echo "Installing backend dependencies..."
    "$BACKEND_DIR/.venv/bin/python" -m pip install -r "$BACKEND_DIR/requirements.txt"
  fi

  if [[ ! -d "$FRONTEND_DIR/node_modules" ]]; then
    echo "Installing frontend dependencies..."
    (cd "$FRONTEND_DIR" && npm install)
  fi
fi

PYTHON_BIN="$PYTHON_SOURCE"
if [[ -x "$BACKEND_DIR/.venv/bin/python" ]]; then
  PYTHON_BIN="$BACKEND_DIR/.venv/bin/python"
fi

BACKEND_LOG="$LOG_DIR/backend.log"
BACKEND_ERROR_LOG="$LOG_DIR/backend.err.log"
FRONTEND_LOG="$LOG_DIR/frontend.log"
FRONTEND_ERROR_LOG="$LOG_DIR/frontend.err.log"
UNITY_RUNNER_LOG="$LOG_DIR/unity-runner.log"
BACKEND_PID_FILE="$ROOT/.dev-backend.pid"
FRONTEND_PID_FILE="$ROOT/.dev-frontend.pid"
UNITY_RUNNER_PID_FILE="$ROOT/.dev-unity-runner.pid"
UNITY_RUNNER_PROFILE="$ROOT/.unity-runner-profile"

prepare_pid_file "backend" "$BACKEND_PID_FILE"
prepare_pid_file "frontend" "$FRONTEND_PID_FILE"
prepare_pid_file "Unity WebGL runner" "$UNITY_RUNNER_PID_FILE"
ensure_port_available 8000
ensure_port_available 3000

start_process "backend" "$BACKEND_DIR" "$BACKEND_LOG" "$BACKEND_ERROR_LOG" "$BACKEND_PID_FILE" \
  "$PYTHON_BIN" -m uvicorn main:app --host 127.0.0.1 --port 8000
start_process "frontend" "$FRONTEND_DIR" "$FRONTEND_LOG" "$FRONTEND_ERROR_LOG" "$FRONTEND_PID_FILE" \
  npm run dev -- --hostname 127.0.0.1 --port 3000

if ! wait_for_url "http://127.0.0.1:8000/api/health" 60 "${SMARTPARKING_LOCAL_API_KEY:-local-dev-key}"; then
  echo "Backend did not become ready. See $BACKEND_LOG and $BACKEND_ERROR_LOG." >&2
  exit 1
fi
if ! wait_for_url "http://127.0.0.1:3000"; then
  echo "Frontend did not become ready. See $FRONTEND_LOG and $FRONTEND_ERROR_LOG." >&2
  exit 1
fi

start_unity_runner

echo
echo "SmartParking dev servers are running."
echo "User:  http://127.0.0.1:3000"
echo "Admin: http://127.0.0.1:3000/admin"
echo "API:   http://127.0.0.1:8000/api/health"
echo
echo "Logs:"
echo "  $BACKEND_LOG"
echo "  $BACKEND_ERROR_LOG"
echo "  $FRONTEND_LOG"
echo "  $FRONTEND_ERROR_LOG"
echo "  $UNITY_RUNNER_LOG"
echo
echo "Stop:"
echo "  ./scripts/stop-dev.sh"
