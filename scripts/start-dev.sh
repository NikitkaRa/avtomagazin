#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

echo "Starting infra (Postgres + RabbitMQ)..."
docker compose up -d

echo "Waiting for Postgres..."
until docker compose exec -T postgres pg_isready -U avtomagazin >/dev/null 2>&1; do sleep 1; done

echo "Launch services in background logs under ./.run-logs"
mkdir -p .run-logs
: > .run-logs/pids

run_svc() {
  local name="$1"
  local project="$2"
  local port="$3"
  local bind="${4:-127.0.0.1}"
  nohup env ASPNETCORE_ENVIRONMENT=Development \
    dotnet run --project "$project" --no-launch-profile \
    --urls "http://${bind}:${port}" \
    >".run-logs/${name}.log" 2>&1 &
  local pid=$!
  disown "$pid" 2>/dev/null || true
  echo "$pid" >> .run-logs/pids
  echo "  $name → ${bind}:${port} (pid $pid)"
}

run_svc identity src/Services/Identity/Avtomagazin.Identity.Api 5101
run_svc fleet src/Services/Fleet/Avtomagazin.Fleet.Api 5102
run_svc routing src/Services/Routing/Avtomagazin.Routing.Api 5103
run_svc notifications src/Services/Notifications/Avtomagazin.Notifications.Api 5104
run_svc gateway src/Gateway/Avtomagazin.Gateway 5100 0.0.0.0
run_svc admin src/Apps/Avtomagazin.Admin 5200 0.0.0.0

echo "Ready. Локально, без серверов."
echo "  Персонал: http://127.0.0.1:5200"
echo "  Gateway:  http://127.0.0.1:5100"
echo "  Житель:   ./scripts/run-android.sh resident"
lan="$(ipconfig getifaddr en0 2>/dev/null || true)"
if [[ -n "${lan}" ]]; then
  echo "  В Wi‑Fi сети админка: http://${lan}:5200"
fi
echo "Сценарий показа: DEMO.md"
echo "Stop with: ./scripts/stop-dev.sh"
