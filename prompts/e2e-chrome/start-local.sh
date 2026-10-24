#!/usr/bin/env bash
# Starts Flowamaz locally for the Chrome E2E run (macOS / Linux / WSL / Git Bash).
#   ./prompts/e2e-chrome/start-local.sh          start everything
#   ./prompts/e2e-chrome/start-local.sh stop     stop API + web (datastores keep running)
# Optional: export ANTHROPIC_KEY=sk-ant-... first to exercise the AI creation journeys (J11-J13).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
LOGS="$ROOT/prompts/e2e-chrome/results/logs"
mkdir -p "$LOGS"

# PIDs listening on a TCP port (lsof on macOS/Linux; fuser as a fallback).
port_pids() {
  if command -v lsof >/dev/null 2>&1; then lsof -nP -ti "tcp:$1" -sTCP:LISTEN 2>/dev/null || true
  elif command -v fuser >/dev/null 2>&1; then fuser -n tcp "$1" 2>/dev/null | tr -s ' ' '\n' | grep -E '^[0-9]+$' || true
  fi
}

# The pid files hold the `dotnet run` / `npx` launchers; the real servers are their children, so
# stop the launchers' process trees AND whatever still listens on the app ports.
stop_app() {
  for f in "$LOGS"/api.pid "$LOGS"/web.pid; do
    if [[ -f $f ]]; then
      pid=$(cat "$f")
      pkill -TERM -P "$pid" 2>/dev/null || true
      kill -TERM "$pid" 2>/dev/null || true
      rm -f "$f"
    fi
  done
  for port in 8307 5173; do
    pids=$(port_pids "$port")
    [[ -n "$pids" ]] && kill -TERM $pids 2>/dev/null || true
  done
  for i in {1..10}; do
    [[ -z "$(port_pids 8307)$(port_pids 5173)" ]] && return 0
    sleep 1
  done
  for port in 8307 5173; do pids=$(port_pids "$port"); [[ -n "$pids" ]] && kill -KILL $pids 2>/dev/null || true; done
  sleep 1
  if [[ -n "$(port_pids 8307)$(port_pids 5173)" ]]; then
    echo "Could not free ports 8307/5173. Check with: lsof -nP -i tcp:8307 -i tcp:5173"; return 1
  fi
}

if [[ "${1:-}" == "stop" ]]; then
  stop_app && echo "Stopped API and web (ports 8307 and 5173 are free)."; exit $?
fi

# A previous run (or a stale server) still holding the ports would answer instead of the new build.
if [[ -n "$(port_pids 8307)$(port_pids 5173)" ]]; then
  echo "0/4 Ports 8307/5173 are in use — stopping the previous Flowamaz API/web first…"
  stop_app
fi

echo "1/4 Postgres + Redis (Docker)…"
docker compose -f "$ROOT/infrastructure/docker-compose.dev.yml" up -d
for i in {1..30}; do docker exec flowamaz-dev-db pg_isready -U flowamaz >/dev/null 2>&1 && break; sleep 2; done

echo "2/4 API on http://localhost:8307 (migrates on start; first build takes a minute)…"
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://localhost:8307
export DB_CONNECTION_STRING="Host=localhost;Port=5442;Database=flowamaz;Username=flowamaz;Password=flowamaz_dev"
export REDIS_CONNECTION_STRING="localhost:6379"
export EDITION=pro                                  # cloud edition: no 5-workflow Community cap
export CORS_ALLOWED_ORIGINS="http://localhost:5173"
export GIT_REPOS_BASE_PATH="$ROOT/prompts/e2e-chrome/results/git-repos"
export CREDENTIAL_MASTER_KEY="e2e-local-only-credential-master-key-0123456789abcdef"   # dev only
export GATE_SIGNING_KEY="e2e-local-only-gate-signing-key-0123456789abcdef0123"          # dev only
export Ai__AllowStubWithoutKey=true                 # non-generation AI features answer with canned text
if [[ -n "${ANTHROPIC_KEY:-}" ]]; then
  code=$(curl -s -o /dev/null -w "%{http_code}" https://api.anthropic.com/v1/models \
    -H "x-api-key: $ANTHROPIC_KEY" -H "anthropic-version: 2023-06-01" || echo 000)
  if [[ "$code" == "200" ]]; then
    export Ai__AnthropicPlatformKey="$ANTHROPIC_KEY"; echo "    (Anthropic key verified — AI journeys enabled)"
  else
    echo "    WARNING: Anthropic rejected ANTHROPIC_KEY (HTTP $code). Starting in AI stub mode; J11–J13 will be BLOCKED."
    echo "             Fix the key and re-run this script to test AI creation."
  fi
fi
( cd "$ROOT/backend" && nohup dotnet run --project Flowamaz.Api --no-launch-profile > "$LOGS/api.log" 2>&1 & echo $! > "$LOGS/api.pid" )
for i in {1..90}; do curl -sf http://localhost:8307/health >/dev/null 2>&1 && break; sleep 3; done
curl -sf http://localhost:8307/health >/dev/null || { echo "API did not come up — see $LOGS/api.log"; exit 1; }

echo "3/4 Web on http://localhost:5173…"
( cd "$ROOT/web" && [[ -d node_modules ]] || npm ci )
( cd "$ROOT/web" && nohup npx vite --port 5173 > "$LOGS/web.log" 2>&1 & echo $! > "$LOGS/web.pid" )
for i in {1..30}; do curl -sf http://localhost:5173/api/v1/templates >/dev/null 2>&1 && break; sleep 2; done

echo "4/4 Checks"
curl -sf -o /dev/null http://localhost:8307/health && echo "    API health ............ OK"
curl -sf -o /dev/null http://localhost:5173/api/v1/templates && echo "    Web → API proxy ....... OK"
echo
echo "Ready. Logs: $LOGS/api.log, $LOGS/web.log"
echo "Next: in the repo root run  claude --chrome  and paste the prompt from prompts/e2e-chrome/README.md"
