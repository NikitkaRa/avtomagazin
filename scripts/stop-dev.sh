#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

if [[ -f .run-logs/pids ]]; then
  while read -r pid; do
    [[ -n "$pid" ]] || continue
    kill "$pid" 2>/dev/null || true
  done < .run-logs/pids
  rm -f .run-logs/pids
  echo "Stopped API processes."
else
  echo "No .run-logs/pids — nothing to stop."
fi

for port in 5100 5101 5102 5103 5104 5200 5201; do
  pid=$(lsof -tiTCP:$port -sTCP:LISTEN 2>/dev/null || true)
  if [[ -n "${pid:-}" ]]; then
    kill $pid 2>/dev/null || true
  fi
done
