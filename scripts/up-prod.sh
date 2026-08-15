#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

if [[ ! -f .env ]]; then
  cp .env.example .env
  echo "Created .env from example."
fi

fill() {
  local key="$1"
  local current
  current="$(grep -E "^${key}=" .env | cut -d= -f2- || true)"
  if [[ -n "${current}" ]]; then
    return
  fi
  local value
  value="$(openssl rand -base64 48 | tr -d '\n=/+' | head -c 48)"
  if grep -qE "^${key}=" .env; then
    sed -i.bak "s|^${key}=.*|${key}=${value}|" .env && rm -f .env.bak
  else
    echo "${key}=${value}" >> .env
  fi
  echo "Generated ${key}"
}

fill JWT_KEY
fill INTERNAL_KEY
fill POSTGRES_PASSWORD

# shellcheck disable=SC1091
set -a
source .env
set +a

if [[ -z "${ADMIN_EMAIL:-}" || -z "${ADMIN_PASSWORD:-}" ]]; then
  echo "Впиши в .env ADMIN_EMAIL и ADMIN_PASSWORD (от 8 символов) и запусти снова."
  exit 1
fi
if [[ ${#ADMIN_PASSWORD} -lt 8 ]]; then
  echo "ADMIN_PASSWORD от 8 символов."
  exit 1
fi
if [[ ${#JWT_KEY} -lt 32 ]]; then
  echo "JWT_KEY от 32 символов."
  exit 1
fi

docker compose -f docker-compose.prod.yml up -d --build

echo "Waiting for gateway..."
for _ in $(seq 1 90); do
  if curl -sf http://127.0.0.1:8080/health >/dev/null; then
    echo "API  http://127.0.0.1:8080"
    echo "Admin http://127.0.0.1:8081  (${ADMIN_EMAIL})"
    echo "Cloudflare Full → эти порты."
    exit 0
  fi
  sleep 2
done

echo "Gateway не ответил на :8080/health. Смотри: docker compose -f docker-compose.prod.yml logs"
exit 1
