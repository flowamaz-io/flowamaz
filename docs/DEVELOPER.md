# Flowamaz Developer Guide

This guide is for engineers working on the Flowamaz platform itself. For deploying Flowamaz, see
[DEPLOYMENT.md](./DEPLOYMENT.md); for day-to-day local setup, see [DEVELOPMENT.md](./DEVELOPMENT.md).

## Architecture

Flowamaz is a clean-architecture .NET solution plus a Vue 3 SPA.

```
backend/
  Flowamaz.Api            HTTP layer — controllers, middleware, auth, DI composition root
  Flowamaz.Application     use cases — services, validators, orchestrator, AI, workflow engine
  Flowamaz.Core            domain — entities, enums, interfaces, exceptions, models (no deps)
  Flowamaz.Infrastructure  adapters — EF Core/Postgres, Redis, git (LibGit2Sharp), workers, jobs
web/                       app SPA (flowamaz.io app) — Vue 3, Pinia, Tailwind v4
marketing/                 marketing SPA (flowamaz.com)
```

Dependency rule: `Api → Application → Core` and `Infrastructure → Core`. **Core depends on nothing.**
Controllers never touch EF Core, LibGit2Sharp, or Redis directly — always through an Application
service or a Core interface. Workspace-scoped queries always filter by `workspace_id`.

Key cross-cutting pieces:
- **AutoWrapper** wraps every response; **FluentValidation** validates request DTOs.
- **Serilog** structured JSON with entry/exit/error on every function.
- **GlobalExceptionMiddleware** maps `AppException` subclasses to actionable HTTP responses.
- **Quartz.NET** (timers/schedules) and **Hangfire** (webhook delivery, metering) for background work.
- Durable execution lives in `WorkflowOrchestrator` + `OrchestratorWorker` (Redis-backed task queue,
  SKIP LOCKED claim, append-only event log).

## Running locally

Prerequisites: .NET 10 SDK, Node 22+, Docker.

```bash
# 1. Datastores (Postgres 5442, Redis 6379)
docker compose -f infrastructure/docker-compose.dev.yml up -d

# 2. Backend (http://localhost:8307) — applies migrations on start
cd backend
dotnet ef database update --project Flowamaz.Infrastructure --startup-project Flowamaz.Api
dotnet watch --project Flowamaz.Api

# 3. App SPA (http://localhost:5173)
cd web && npm install && npm run dev

# 4. Verify
curl http://localhost:8307/health     # database + redis must be Healthy
open http://localhost:8307/scalar      # API reference
```

The full stack (proxy, backend, web, db, redis) runs via `infrastructure/docker-compose.yml`.

## Running tests

```bash
# Backend unit tests
cd backend && dotnet test Flowamaz.Tests.Unit/Flowamaz.Tests.Unit.csproj

# Backend integration tests (spins up Postgres + Redis via Testcontainers — Docker required)
dotnet test Flowamaz.Tests.Integration/Flowamaz.Tests.Integration.csproj

# Web unit tests (Vitest) and type-check
cd web && npm run test && npm run typecheck

# End-to-end (Playwright)
cd web && npm run e2e
```

CI runs build + unit + integration + web on every PR to `develop`.

## Making changes

- Branch from `develop`: `feat/phase-{NN}-prompt-{NN}-{slug}` (or `fix/...`).
- Conventional commits: `feat(scope): description`, `fix(scope): ...`, `docs: ...`.
- `develop` is protected — land changes via PR. CI must be green.
- Every changed line should trace to the request; match existing style; no speculative abstraction.

## Adding a connector

1. **Manifest** — add the connector definition (slug, auth type, actions) to the seed in
   `Flowamaz.Infrastructure` connector seeding.
2. **Handler** — implement the connector's action handler in `Flowamaz.Application` against the
   connector execution interface; resolve credentials through the vault (never inline secrets).
3. **Tests** — unit-test the handler (success + failure + credential masking).
4. **Seed** — register it so it appears in the Library; add a help article under
   `web/src/help/articles/connectors/`.

## Adding a workflow node type

1. **Enum** — add the case to `Flowamaz.Core/Enums/NodeType.cs`.
2. **Parser** — ensure `SfgParser` accepts the new `type` (and any hyphenated alias).
3. **Executor** — implement the node worker and register it in `INodeWorkerRegistry`; the
   orchestrator surfaces unknown executable nodes as `Pending` until a worker exists.
4. **Canvas** — add the node's icon/colour to the Cytoscape style map and the node palette in `web/`.
5. **Docs** — add a help article under `web/src/help/articles/node-types/`.

## Environment variables reference

| Variable | Description | Example |
|----------|-------------|---------|
| `DB_CONNECTION_STRING` | Postgres connection string | `Host=db;Database=flowamaz;Username=flowamaz;Password=...` |
| `DB_NAME` / `DB_USER` / `DB_PASSWORD` | Compose Postgres credentials | `flowamaz` / `flowamaz` / `s3cret` |
| `REDIS_CONNECTION_STRING` | Redis (queue, cache, rate limit) | `redis:6379` |
| `JWT_SECRET` | HMAC signing key for access tokens (≥32 bytes) | `openssl rand -base64 48` |
| `JWT_ISSUER` / `JWT_AUDIENCE` | Token issuer / audience | `flowamaz.io` / `flowamaz-app` |
| `CREDENTIAL_MASTER_KEY` | AES key for the connector credential vault | `openssl rand -base64 32` |
| `GATE_SIGNING_KEY` | HMAC key for signed human-gate email/Slack links | `openssl rand -base64 32` |
| `Ai__AnthropicPlatformKey` (`ANTHROPIC_PLATFORM_KEY`) | Platform Anthropic API key | `sk-ant-...` |
| `Email__ResendApiKey` (`RESEND_API_KEY`) | Resend transactional email key | `re_...` |
| `EMAIL_FROM_ADDRESS` | From address for transactional mail | `noreply@flowamaz.io` |
| `STRIPE_SECRET_KEY` | Stripe secret key (billing) | `sk_live_...` |
| `STRIPE_WEBHOOK_SECRET` | Stripe webhook signing secret | `whsec_...` |
| `Platform__BaseUrl` | App base URL (redirect allowlist, links) | `https://app.flowamaz.io` |
| `EDITION` | `community` \| `starter` \| `pro` \| `enterprise` | `pro` |
| `GIT_REPOS_BASE_PATH` | Base path for per-workspace bare git repos | `/app/data/repos` |
| `CORS_ALLOWED_ORIGINS` | Comma-separated allowed origins | `https://app.flowamaz.io` |
| `SLACK_CLIENT_ID` / `SLACK_CLIENT_SECRET` | Slack OAuth app (connector + gate delivery) | `123...` / `abc...` |

In Development these may be omitted (sensible defaults / stubs). In every non-Development
environment, startup validation fails fast if any of the six required secrets are missing:
`DB_CONNECTION_STRING`, `JWT_SECRET`, `CREDENTIAL_MASTER_KEY`, `GATE_SIGNING_KEY`,
`Email__ResendApiKey`, `Ai__AnthropicPlatformKey`.

## Troubleshooting

- **CORS errors in the browser** — set `CORS_ALLOWED_ORIGINS` to include the SPA origin. With no
  value, non-Development refuses all cross-origin requests by design.
- **Auth cookie / 401 loops** — confirm `JWT_SECRET` matches between restarts and the SPA points at
  the right `VITE_API_BASE_URL`.
- **`docker compose up` fails / disk space** — `docker system prune` to reclaim space; Testcontainers
  leaves no containers but does pull images.
- **Port conflicts** — backend `8307`, app `5173`, marketing `5174`, Postgres `5442`, Redis `6379`.
  Change the dev compose file or the Vite `server.port` if occupied.
- **`has-pending-model-changes`** — you edited an entity without a migration; run
  `dotnet ef migrations add <Name> ...` (see DEVELOPMENT.md).
- **Health is Degraded but app works** — external dependency checks (Stripe/Anthropic/Resend) are
  non-critical and never 503 the endpoint; only `database`/`redis` being down returns 503.
