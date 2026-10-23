Read CLAUDE.md, checkpoint.md, prompts/fix-test-02/README.md, results.md (last entry).

GOAL: Make the Playwright suite (web/e2e, S1–S80) actually EXECUTE end to end, locally and in
CI. Passing is NOT the goal of this prompt — a real run with real results is.

--- FIX 1: Vite dev proxy ---
web/vite.config.ts — add under server:
  proxy: {
    '/api':    { target: process.env.VITE_DEV_API_TARGET ?? 'http://localhost:5000', changeOrigin: true },
    '/health': { target: process.env.VITE_DEV_API_TARGET ?? 'http://localhost:5000', changeOrigin: true },
    '/scalar': { target: process.env.VITE_DEV_API_TARGET ?? 'http://localhost:5000', changeOrigin: true },
  }
Include any other backend paths the SPA calls relatively (grep web/src for fetch/axios paths
not under /api — e.g. websocket/SSE endpoints; add `ws: true` where needed).
Verify manually: npm run dev, register in the browser, workspace list loads.

--- FIX 2: Migrations in the E2E workflow ---
.github/workflows/e2e.yml — after dependencies are up and before starting the API:
  dotnet tool install --global dotnet-ef
  dotnet ef database update --project backend/Flowamaz.Infrastructure --startup-project backend/Flowamaz.Api
Use the same connection string env the API uses. Do NOT add auto-migrate-on-startup to
Program.cs for production; if you add it, gate it behind IsDevelopment() AND an explicit
config flag, default off.

--- FIX 3: Readiness instead of sleep ---
Replace `sleep 20` with:
  - build first: dotnet build backend/Flowamaz.Api -c Release, then run with --no-build
  - npx wait-on -t 180000 http://localhost:5000/health http://localhost:5173
Same for web. Also set every env var the API hard-requires in Development (check .env.example
and startup validation) so it does not exit on boot. Write API stdout to a file and upload it
as an artifact on failure, alongside playwright-report.

--- FIX 4: One config ---
There are two Playwright configs (web/playwright.config.ts and web/e2e/playwright.config.ts).
npm run e2e uses web/e2e/playwright.config.ts. Delete the stale root one, or make it re-export
the e2e one. Set reporter to ['list', ['html', { open: 'never' }], ['json', { outputFile: 'e2e-results.json' }]].

--- FIX 5: Local one-command runner ---
Create scripts/e2e-local.sh that: starts dev deps, migrates a fresh DB, builds+starts the API,
starts web, waits on health, runs `npm run e2e --prefix web`, and always tears down.
Document it in docs/DEVELOPMENT.md.

--- RUN ---
Run scripts/e2e-local.sh to completion. Then trigger the CI workflow:
  gh workflow enable E2E
  gh workflow run E2E --ref develop
Wait for it and confirm the run reaches the test step with the API healthy.

DELIVERABLE: results.md entry "fix-test-02-02 E2E harness" with: total / passed / failed /
skipped counts from e2e-results.json, local vs CI, and confirmation that zero failures are
caused by connection refused / 404 on /api / empty DB. If any are, the harness is not fixed yet.
Update checkpoint.md → test-03-e2e-triage.
Commit: test(e2e): working harness — proxy, migrations, readiness, local runner [fix-test-02-02]
