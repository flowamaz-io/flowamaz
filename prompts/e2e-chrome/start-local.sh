#!/usr/bin/env bash
# Starts Flowamaz locally for the Chrome E2E run (macOS / Linux / WSL / Git Bash).
#   ./prompts/e2e-chrome/start-local.sh          start everything
#   ./prompts/e2e-chrome/start-local.sh stop     stop API + web (datastores keep running)
# Optional: export ANTHROPIC_KEY=sk-ant-... first to exercise the AI creation journeys (J11-J13).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
LOGS="$ROOT/prompts/e2e-chrome/results/logs"
mkdir -p "$LOGS"

if [[ "${1:-}" == "stop" ]]; then
  for f in "$LOGS"/api.pid "$LOGS"/web.pid; do [[ -f $f ]] && kill "$(cat "$f")" 2>/dev/null || true; rm -f "$f"; done
  echo "Stopped API and web."; exit 0
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
if [[ -n "${ANTHROPIC_KEY:-}" ]]; then export Ai__AnthropicPlatformKey="$ANTHROPIC_KEY"; echo "    (real Anthropic key set — AI journeys enabled)"; fi
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
