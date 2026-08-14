#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

echo "==> Unit + integration tests"
dotnet test Avtomagazin.sln --nologo

echo "==> Waiting for gateway"
for i in $(seq 1 60); do
  if curl -sf http://localhost:5100/health >/dev/null; then
    break
  fi
  sleep 1
done

if ! curl -sf http://localhost:5100/health >/dev/null; then
  echo "Gateway is not up on :5100 — start stack with ./scripts/start-dev.sh"
  exit 1
fi

echo "==> Newman (Postman)"
newman run postman/Avtomagazin.postman_collection.json --timeout-request 10000
