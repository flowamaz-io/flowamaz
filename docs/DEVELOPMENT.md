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
Defaults: Postgres `localhost:5432` (db/user/password `flowamaz`/`flowamaz`/`flowamaz_dev`),
Redis `localhost:6379`. These match `backend/Flowamaz.Api/appsettings.Development.json`.

## Backend
```bash
cd backend
# Apply migrations to the dev database
dotnet ef database update --project Flowamaz.Infrastructure --startup-project Flowamaz.Api
# Run (http://localhost:5000) with hot reload
dotnet watch --project Flowamaz.Api
```
- API docs (Scalar): http://localhost:5000/scalar
- Health: http://localhost:5000/health
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
Set `VITE_API_BASE_URL=http://localhost:5000` in `web/.env` (see `web/.env.example`).

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
