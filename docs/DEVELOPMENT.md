# Development

## Prerequisites
- .NET 10 SDK
- Node 22+
- Docker (for Postgres/Redis and for the integration test suite via Testcontainers)

## Dependencies (Postgres + Redis)
The app runs on the host during development; only the datastores run in Docker:
```bash
docker compose -f infrastructure/docker-compose.dev.yml up -d
docker compose -f infrastructure/docker-compose.dev.yml down   # stop
```
Defaults: Postgres `localhost:5442` (db/user/password `flowamaz`/`flowamaz`/`flowamaz_dev`),
Redis `localhost:6379`. These match `backend/Flowamaz.Api/appsettings.Development.json`.

## Backend
```bash
cd backend
# Apply migrations to the dev database
dotnet ef database update --project Flowamaz.Infrastructure --startup-project Flowamaz.Api
# Run (http://localhost:8307) with hot reload
dotnet watch --project Flowamaz.Api
```
- API docs (Scalar): http://localhost:8307/scalar
- Health: http://localhost:8307/health
- Add a migration: `dotnet ef migrations add <Name> --project Flowamaz.Infrastructure --startup-project Flowamaz.Api --output-dir Persistence/Migrations`

### Debugging individual concerns
- Logs: structured JSON to console + `backend/Flowamaz.Api/logs/flowamaz-*.log`.
- JWT: set `Jwt__Secret` (env `JWT_SECRET`, ≥ 32 chars). In Development a random ephemeral key is generated if unset (tokens won't survive a restart).

## Frontend
```bash
cd web
npm install
npm run dev          # http://localhost:5173 (proxies API via VITE_API_BASE_URL)
npm run typecheck
npm run build
npm run lint
```
Set `VITE_API_BASE_URL=http://localhost:8307` in `web/.env` (see `web/.env.example`).

## Tests
```bash
# Backend unit tests (fast, no Docker)
cd backend && dotnet test Flowamaz.Tests.Unit

# Backend integration tests — require Docker (Testcontainers spins up Postgres + Redis)
dotnet test Flowamaz.Tests.Integration

# Frontend
cd web && npm run test
```
The integration suite runs sequentially (container-backed) and boots the real API via
`WebApplicationFactory`, swapping persistence/cache to throwaway containers.

---

## Continuous Integration (GitHub Actions)

CI is defined in `.github/workflows/`:

- **ci.yml** — runs on every push to `develop`, `feat/**`, `fix/**` and on PRs into `main`/`develop`.
  Jobs: `backend` (Postgres + Redis service containers, `dotnet build/test`, coverage → Codecov),
  `web` (typecheck → lint → test → build), `cli` (build + vitest), `vscode-extension` (build + vitest),
  `security` (Trivy fs scan, fails on CRITICAL/HIGH).
- **release.yml** — on push to `main`: builds and pushes `flowamaz-backend` / `flowamaz-web` images to
  GHCR and publishes `@flowamaz/cli` to npm.
- **e2e.yml** — Playwright suite, nightly at 02:00 UTC and on manual `workflow_dispatch`.

### Required status checks (branch protection)

Configure on `flowamaz-io/flowamaz` (Settings → Rules) — these are **enabled manually by a repo admin**;
the workflows above provide the checks:

- `develop` ruleset → required checks: `backend`, `web`, `cli`, `vscode-extension`, `security`
- `main` ruleset → the same **plus** `build-and-push`

### Running CI locally

Install [`act`](https://github.com/nektos/act) and run a job:

```bash
act -j web                 # run the web job
act pull_request -j backend # run the backend job as a PR event
```

`act` needs Docker running. The backend job's Postgres/Redis service containers work under `act`
with the `--container-architecture linux/amd64` flag on Apple Silicon.

### Secrets

- `NPM_TOKEN` — npm publish token for `@flowamaz/cli` (release only)
- `CODECOV_TOKEN` — optional, coverage upload (the step is `continue-on-error`)
- `GITHUB_TOKEN` — auto-provided by Actions; used for GHCR push
