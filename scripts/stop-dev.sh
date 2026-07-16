#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

stop_process_tree() {
  local pid="$1"
  local child

  while IFS= read -r child; do
    [[ -n "$child" ]] && stop_process_tree "$child"
  done < <(pgrep -P "$pid" 2>/dev/null || true)

  if kill -0 "$pid" 2>/dev/null; then
    kill "$pid" 2>/dev/null || true
    echo "Stopped PID=$pid"
  fi
}

for pid_file in "$ROOT/.dev-backend.pid" "$ROOT/.dev-frontend.pid" "$ROOT/.dev-unity-runner.pid"; do
  [[ -f "$pid_file" ]] || continue

  pid="$(head -n 1 "$pid_file")"
  if [[ -n "$pid" ]]; then
    stop_process_tree "$pid"
  fi

  rm -f "$pid_file"
done
