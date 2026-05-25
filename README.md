# Flowamaz

AI-native workflow orchestration platform. Describe what you want in plain English, watch it
build itself, and trust it to run reliably.

## Stack
- **Backend** — .NET 10, clean architecture (Api / Application / Core / Infrastructure), EF Core + PostgreSQL, Redis, Serilog, Scalar API docs.
- **Frontend** — Vue 3 (Composition API, TypeScript strict), Vite, Tailwind CSS v4, Pinia.
- **Infra** — Docker + Docker Compose, nginx reverse proxy (TLS, rate limiting, security headers).

## Quick start

### Prerequisites
- Docker + Docker Compose
- For local (non-container) dev: .NET 10 SDK, Node 22+

### Local development
```bash
cp .env.example .env            # then edit .env with your values

# Start dependencies only (Postgres + Redis)
docker compose -f infrastructure/docker-compose.dev.yml up -d

# Backend (applies migrations, then serves on http://localhost:5000)
cd backend
dotnet ef database update --project Flowamaz.Infrastructure --startup-project Flowamaz.Api
dotnet run --project Flowamaz.Api

# Frontend (http://localhost:5173)
cd web && npm install && npm run dev
```

### Full stack (Docker)
```bash
cp .env.example .env            # set JWT_SECRET (>= 32 chars), DB_* values, etc.
cd infrastructure/nginx/ssl && bash generate-dev-cert.sh && cd ../../..
docker compose -f infrastructure/docker-compose.yml up -d

# App:      https://localhost
# API:      https://localhost/api
# API docs: https://localhost/scalar
# Health:   https://localhost/health
```
On first run, apply migrations against the containerised DB (see `docs/DEPLOYMENT.md`).

### Tests
```bash
cd backend && dotnet test          # unit + integration (integration needs Docker)
cd web && npm run test             # vitest
```

## Documentation
- `docs/DEVELOPMENT.md` — local dev, debugging, hot reload, test setup
- `docs/DEPLOYMENT.md` — production checklist, SSL, DNS/email, backups
- `SECURITY.md` — vulnerability disclosure
- `FUNCTIONAL.md` — complete product requirements
