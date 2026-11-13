#!/usr/bin/env bash
# Runs the Playwright E2E suite (web/e2e) against a throwaway local stack, the same way CI does.
#
#   ./scripts/e2e-local.sh                  whole suite
#   ./scripts/e2e-local.sh -g "S44|S45"     extra arguments go to `playwright test`
#   E2E_KEEP=1 ./scripts/e2e-local.sh        leave the API/web/marketing servers running afterwards
#
# It uses its own ports (API 8317, web 5183, marketing 5184) and its own database (flowamaz_e2e, recreated on every
# run), so it does not touch a dev stack started with prompts/e2e-chrome/start-local.sh. Postgres, Redis and Mailpit
# come from infrastructure/docker-compose.dev.yml. Keys are generated for each run and never written to disk.
# Reports: web/e2e/playwright-report/ (HTML) and web/e2e/e2e-results.json.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
LOGS="$ROOT/web/e2e/.local-run"
API_PORT=8317 WEB_PORT=5183 MKT_PORT=5184
mkdir -p "$LOGS"

PIDS=()
cleanup() {
  [[ "${E2E_KEEP:-}" == "1" ]] && { echo "Servers left running (E2E_KEEP=1). Logs in $LOGS"; return; }
  for pid in "${PIDS[@]}"; do pkill -TERM -P "$pid" 2>/dev/null || true; kill -TERM "$pid" 2>/dev/null || true; done
}
trap cleanup EXIT

wait_for() { # url name seconds log
  for _ in $(seq 1 "$3"); do curl -sf -o /dev/null "$1" && return 0; sleep 1; done
  echo "$2 did not answer at $1 within $3s. Last lines of $4:"; tail -n 30 "$4"; exit 1
}

for port in $API_PORT $WEB_PORT $MKT_PORT; do
  if curl -s -o /dev/null "http://localhost:$port"; then
    echo "Port $port is already in use (an earlier E2E_KEEP=1 run?). Stop that process and run this again."; exit 1
  fi
done

echo "1/5 Postgres + Redis + Mailpit (Docker)…"
docker compose -f "$ROOT/infrastructure/docker-compose.dev.yml" up -d --wait
docker exec flowamaz-dev-db psql -qU flowamaz -d postgres -c 'DROP DATABASE IF EXISTS flowamaz_e2e WITH (FORCE)' -c 'CREATE DATABASE flowamaz_e2e'

echo "2/5 Building the API, web and CLI…"
dotnet build "$ROOT/backend/Flowamaz.Api" -c Release -v q -nologo >"$LOGS/build.log" || { tail -n 40 "$LOGS/build.log"; exit 1; }
(cd "$ROOT/web" && npm ci --silent)
(cd "$ROOT/marketing" && npm ci --silent)
(cd "$ROOT/cli" && npm ci --silent && npm run build --silent)

echo "3/5 API on http://localhost:$API_PORT (migrates on start)…"
(
  export ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:$API_PORT"
  export DB_CONNECTION_STRING="Host=localhost;Port=5442;Database=flowamaz_e2e;Username=flowamaz;Password=flowamaz_dev"
  export REDIS_CONNECTION_STRING="localhost:6379" EDITION=pro
  export CORS_ALLOWED_ORIGINS="http://localhost:$WEB_PORT" Platform__BaseUrl="http://localhost:$WEB_PORT"
  export GIT_REPOS_BASE_PATH="$LOGS/git-repos"
  JWT_SECRET="$(openssl rand -hex 32)" CREDENTIAL_MASTER_KEY="$(openssl rand -hex 32)" GATE_SIGNING_KEY="$(openssl rand -hex 32)"
  export JWT_SECRET CREDENTIAL_MASTER_KEY GATE_SIGNING_KEY
  export Ai__AllowStubWithoutKey=true PublicApi__EnforcePlanRateLimits=false
  export Email__SmtpHost=localhost Email__SmtpPort=1026 Email__FromAddress="noreply@flowamaz.local"
  exec dotnet run --project "$ROOT/backend/Flowamaz.Api" -c Release --no-build --no-launch-profile
) >"$LOGS/api.log" 2>&1 &
PIDS+=($!)

echo "4/5 Web on http://localhost:$WEB_PORT, marketing on http://localhost:$MKT_PORT…"
(cd "$ROOT/web" && VITE_API_PROXY_TARGET="http://localhost:$API_PORT" exec npx vite --port $WEB_PORT --strictPort) >"$LOGS/web.log" 2>&1 &
PIDS+=($!)
(cd "$ROOT/marketing" && exec npx vite --port $MKT_PORT --strictPort) >"$LOGS/marketing.log" 2>&1 &
PIDS+=($!)
wait_for "http://localhost:$API_PORT/health" API 180 "$LOGS/api.log"
wait_for "http://localhost:$WEB_PORT" Web 60 "$LOGS/web.log"
wait_for "http://localhost:$MKT_PORT" Marketing 60 "$LOGS/marketing.log"

echo "5/5 Playwright…"
cd "$ROOT/web"
[[ -n "${E2E_CHROMIUM_PATH:-}" ]] || npx playwright install chromium >/dev/null
E2E_API_URL="http://localhost:$API_PORT" E2E_BASE_URL="http://localhost:$WEB_PORT" E2E_MARKETING_URL="http://localhost:$MKT_PORT" \
  npx playwright test --config e2e/playwright.config.ts "$@"
