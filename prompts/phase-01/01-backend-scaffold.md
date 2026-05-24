---
prompt-id: 01-backend-scaffold
phase: 01
sequence: 1
roles: [Executor, Verifier, Security]
type: feature
depends-on: []
estimated-complexity: High
---

# Backend Scaffold — .NET Solution, Clean Architecture, Full Infrastructure

## Context
First prompt. Nothing exists. This builds the entire backend foundation that every
subsequent prompt depends on. Read FUNCTIONAL.md §3 (tech stack) and §5 (AI model
governance) before starting. All decisions are locked — do not deviate.

## Objective
Create the complete .NET solution with clean architecture, EF Core, PostgreSQL, Redis,
Serilog, Scalar, JWT infrastructure, email service (Resend), AI cost control
infrastructure, global exception handling, and all base abstractions.

## Scope

### What to Build

**Solution structure:**
```
Flowamaz.sln
backend/
  Flowamaz.Api/               — Controllers, middleware, program.cs
  Flowamaz.Core/              — Entities, interfaces, enums, models
  Flowamaz.Application/       — Services, DTOs, validators, use cases
  Flowamaz.Infrastructure/    — EF Core, repositories, external services
  Flowamaz.Tests.Unit/        — xUnit unit tests
  Flowamaz.Tests.Integration/ — xUnit integration tests (Testcontainers)
```

**Base entity hierarchy (Flowamaz.Core/Entities/):**
- `BaseEntity` — Id (Guid), CreatedAt, UpdatedAt, IsDeleted, DeletedAt
- `AuditableEntity : BaseEntity` — CreatedBy (Guid?), UpdatedBy (Guid?)
- `WorkspaceEntity : AuditableEntity` — WorkspaceId (Guid, NOT nullable, always filtered)

**Core infrastructure (Flowamaz.Infrastructure/):**
- `FlowAmazDbContext` — snake_case EF naming convention, global soft-delete
  query filter on BaseEntity, UserId from ICurrentUserService for audit fields
- `RepositoryBase<T>` — async CRUD, workspace_id auto-filter on WorkspaceEntity,
  never allows raw query without workspace_id on scoped entities
- Redis — IConnectionMultiplexer singleton, registered with connection string from config
- Serilog — structured JSON output, console + rolling file sink, CorrelationId enricher,
  entry/exit/error on every public service method (using logging source generator or manual)
- AutoWrapper — all responses wrapped, consistent error shape
- FluentValidation — pipeline behaviour, validates all request DTOs before handler
- Global exception middleware — maps exceptions to structured error responses,
  no stack traces ever returned to client, all errors logged with correlation ID
- CorrelationId middleware — generates X-Correlation-Id on every request,
  adds to response header and Serilog context
- CORS — configurable via CORS_ALLOWED_ORIGINS env var
- Health check — GET /health returns 200 with db and redis dependency status

**AI Cost Control Infrastructure (no AI calls yet — just the data layer and services):**

Tables:
- `ai_token_usage` — id, org_id, workspace_id, function_id (F1-F7), model_id,
  provider, tokens_input, tokens_output, cost_usd, created_at
- `workspace_ai_budget` — workspace_id (PK), monthly_token_limit,
  tokens_used_this_month, budget_reset_date, is_hard_capped, alert_sent_at_80pct
- `platform_ai_config` — function_id (PK), provider, model_id, key_source
  (enum: Platform/Byok), is_enabled, plan_access (jsonb string[])
- `org_ai_config` — org_id (PK), providers_enabled (jsonb string[]),
  function_overrides (jsonb), workspace_locked_functions (jsonb string[])
- `workspace_ai_config` — workspace_id (PK), function_overrides (jsonb),
  byok_credential_refs (jsonb)
- `model_catalogue` — model_id (PK), provider, display_name, has_vision (bool),
  max_context_tokens (int), supports_json_mode (bool), supports_streaming (bool),
  is_enabled (bool), plan_access (jsonb string[])

Services (interfaces in Core, implementations in Infrastructure):
- `IAiTokenMeteringService` — RecordUsageAsync(functionId, modelId, provider,
  workspaceId, tokensInput, tokensOutput, costUsd): fire-and-forget, never blocks
  request path. Uses Task.Run with error logging.
- `IModelResolutionService` — ResolveModelConfigAsync(functionId, workspaceId)
  → ModelConfig {ModelId, Provider, ApiKeySource, ApiKey}. Walks: workspace →
  org → platform. Validates capability requirements (F3 needs has_vision=true,
  F7 needs max_context_tokens >= 100000). Rejects at config time, not call time.
- `IRateLimitService` — CheckAndIncrementAsync(key, maxCount, windowSeconds) → bool.
  Redis sliding window. Used for Co-pilot rate limiting (60/user/hour).
- `ISemanticCacheService` — GetAsync(hash) → string?, SetAsync(hash, value, ttlHours).
  Redis with TTL. Key = SHA256(functionId + workspaceId + normalisedCommand).
- `IEmailService` — SendAsync(to, subject, htmlBody, textBody). Uses Resend API
  (HTTP POST to api.resend.com). From: noreply@flowamaz.io. Configured via
  RESEND_API_KEY env var. Logs send attempts, never throws — logs errors instead.

**Seed data (in EF Core migration — fixed Guids, idempotent):**

Model catalogue — all models from FUNCTIONAL.md §5.1:
- claude-haiku-4-5 (Anthropic, no vision, 200k ctx)
- claude-sonnet-4-6 (Anthropic, vision=true, 200k ctx)
- claude-opus-4-6 (Anthropic, vision=true, 200k ctx)
- gpt-4o (Azure, vision=true, 128k ctx)
- gpt-4o-mini (Azure, no vision, 128k ctx)
- gemini-2-0-flash (Google, vision=true, 1M ctx)
- gemini-1-5-pro (Google, vision=true, 1M ctx)
- moonshot-v1-128k (Kimi, no vision, 128k ctx)
- moonshot-v1-32k (Kimi, no vision, 32k ctx)
- mistral-large (Mistral, no vision, 128k ctx)

Platform AI config — 7 functions with defaults from FUNCTIONAL.md §5.2:
- F1 (copilot): claude-haiku-4-5, Anthropic, Platform key
- F2 (nl-yaml): claude-sonnet-4-6, Anthropic, Platform key
- F3 (visual-input): claude-sonnet-4-6, Anthropic, Platform key
- F4 (node-exec): claude-haiku-4-5, Anthropic, Byok
- F5 (process-intel): claude-haiku-4-5, Anthropic, Platform key
- F6 (help-assist): claude-sonnet-4-6, Anthropic, Platform key
- F7 (doc-parse): claude-sonnet-4-6, Anthropic, Platform key

**Configuration (appsettings.json — all values from env, never hardcoded):**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "",
    "Redis": ""
  },
  "Jwt": { "Secret": "", "Issuer": "", "Audience": "",
           "AccessTokenExpiryMinutes": 15, "RefreshTokenExpiryDays": 7 },
  "Ai": { "AnthropicPlatformKey": "", "GooglePlatformKey": "",
          "BudgetDefaultMonthlyTokens": 500000,
          "CopilotRateLimitPerHour": 60, "SemanticCacheTtlHours": 24 },
  "Email": { "ResendApiKey": "", "FromAddress": "noreply@flowamaz.io",
             "FromName": "Flowamaz", "SupportEmail": "support@flowamaz.io",
             "SecurityEmail": "security@flowamaz.io" },
  "GitHub": { "ConnectorsRepo": "flowamaz-io/connectors",
              "TemplatesRepo": "flowamaz-io/templates",
              "WebhookSecret": "", "AppId": "", "AppPrivateKey": "" },
  "Git": { "ReposBasePath": "/app/data/repos" },
  "Library": { "SyncIntervalMinutes": 15 },
  "Platform": { "BaseUrl": "", "Edition": "community" },
  "Community": { "MaxWorkflows": 5, "MaxRunsPerMonth": 500, "MaxUsers": 1 },
  "HelpDocs": { "BaseUrl": "https://docs.flowamaz.io",
                "GitHubRepo": "flowamaz-io/docs" }
}
```

**Program.cs registration order:**
1. Configuration binding
2. Serilog
3. DbContext (PostgreSQL)
4. Redis
5. All Core interfaces → Infrastructure implementations
6. AI services (metering, model resolution, rate limit, semantic cache)
7. Email service (Resend)
8. FluentValidation (assembly scan)
9. AutoWrapper
10. CORS
11. JWT auth (scheme registration only — middleware added later)
12. Health checks (EF + Redis)
13. Scalar at /scalar (OpenAPI, always public)
14. Middleware pipeline: Serilog → CorrelationId → CORS → Exception → Auth → Controllers

**infrastructure/docker-compose.dev.yml** (postgres + redis only — not the app):
```yaml
services:
  db:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: flowamaz
      POSTGRES_USER: flowamaz
      POSTGRES_PASSWORD: flowamaz_dev
    ports: ["5432:5432"]
    volumes: [pgdata:/var/lib/postgresql/data]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U flowamaz"]
      interval: 5s
  redis:
    image: redis:7-alpine
    ports: ["6379:6379"]
    volumes: [redisdata:/data]
volumes:
  pgdata:
  redisdata:
```

**.gitignore** — .env, .env.local, bin/, obj/, *.user, .vs/, .idea/,
TestResults/, coverage/, data/repos/, *.log, logs/, infrastructure/nginx/ssl/*.pem

### What NOT to Build
- No business entities (org, workspace, user, workflow) — prompts 02-03
- No API endpoints beyond /health and /scalar — prompts 04-05
- No frontend — prompt 06
- No pre-release or preview NuGet packages
- No actual AI calls — infrastructure only

## Technical Requirements

### Backend
- [ ] dotnet build — zero errors, zero warnings, all 6 projects
- [ ] snake_case naming applied via EF Core HasColumnName conventions
- [ ] Global soft-delete query filter on BaseEntity (IsDeleted == false)
- [ ] CorrelationId on every Serilog log entry
- [ ] AutoWrapper registered — all responses wrapped
- [ ] FluentValidation as pipeline behaviour — validates before any handler
- [ ] GET /health → 200 with { db: "healthy", redis: "healthy" }
- [ ] GET /scalar → 200 (no auth required)
- [ ] JWT secret loaded from config — never hardcoded anywhere
- [ ] IEmailService.SendAsync tested with null Resend key → logs warning, does not throw
- [ ] All 6 AI cost tables created in migration with correct indexes
- [ ] model_catalogue seeded with all 10 models
- [ ] platform_ai_config seeded with all 7 functions
- [ ] IModelResolutionService capability validation unit tested

### Database
- [ ] Migration applies cleanly on fresh PostgreSQL 16
- [ ] All FK columns indexed
- [ ] model_catalogue.model_id unique index
- [ ] platform_ai_config.function_id unique index
- [ ] ai_token_usage — index on (workspace_id, created_at) for reporting queries
- [ ] workspace_ai_budget — index on (budget_reset_date) for monthly reset job

## Acceptance Criteria
- [ ] dotnet build — zero errors all 6 projects
- [ ] dotnet test — all unit tests pass
- [ ] GET /scalar → 200
- [ ] GET /health → 200 with db and redis status
- [ ] Migration applies on fresh Postgres (confirmed by integration test)
- [ ] model_catalogue has exactly 10 rows after seed
- [ ] platform_ai_config has exactly 7 rows after seed
- [ ] ModelResolutionService: F3 with haiku model → throws ConfigViolationException (no vision)
- [ ] ModelResolutionService: workspace → org → platform hierarchy works (unit tested)
- [ ] AiTokenMeteringService: RecordUsageAsync does not block — returns immediately
- [ ] EmailService: logs warning when RESEND_API_KEY empty, does not throw
- [ ] No hardcoded secrets in any committed file

## Output Expected
```
Flowamaz.sln
backend/Flowamaz.Api/Program.cs
backend/Flowamaz.Api/Middleware/GlobalExceptionMiddleware.cs
backend/Flowamaz.Api/Middleware/CorrelationIdMiddleware.cs
backend/Flowamaz.Core/Entities/BaseEntity.cs
backend/Flowamaz.Core/Entities/AuditableEntity.cs
backend/Flowamaz.Core/Entities/WorkspaceEntity.cs
backend/Flowamaz.Core/Entities/Ai/AiTokenUsage.cs
backend/Flowamaz.Core/Entities/Ai/WorkspaceAiBudget.cs
backend/Flowamaz.Core/Entities/Ai/PlatformAiConfig.cs
backend/Flowamaz.Core/Entities/Ai/OrgAiConfig.cs
backend/Flowamaz.Core/Entities/Ai/WorkspaceAiConfig.cs
backend/Flowamaz.Core/Entities/Ai/ModelCatalogue.cs
backend/Flowamaz.Core/Interfaces/Services/IAiTokenMeteringService.cs
backend/Flowamaz.Core/Interfaces/Services/IModelResolutionService.cs
backend/Flowamaz.Core/Interfaces/Services/IRateLimitService.cs
backend/Flowamaz.Core/Interfaces/Services/ISemanticCacheService.cs
backend/Flowamaz.Core/Interfaces/Services/IEmailService.cs
backend/Flowamaz.Core/Interfaces/Services/ICurrentUserService.cs
backend/Flowamaz.Infrastructure/Persistence/FlowAmazDbContext.cs
backend/Flowamaz.Infrastructure/Persistence/RepositoryBase.cs
backend/Flowamaz.Infrastructure/Persistence/Migrations/[timestamp]_InitialScaffold.cs
backend/Flowamaz.Infrastructure/Services/AiTokenMeteringService.cs
backend/Flowamaz.Infrastructure/Services/ModelResolutionService.cs
backend/Flowamaz.Infrastructure/Services/RateLimitService.cs
backend/Flowamaz.Infrastructure/Services/SemanticCacheService.cs
backend/Flowamaz.Infrastructure/Services/EmailService.cs
backend/Flowamaz.Tests.Unit/Ai/ModelResolutionServiceTests.cs
backend/Flowamaz.Tests.Unit/Ai/AiTokenMeteringServiceTests.cs
backend/Flowamaz.Tests.Integration/Infrastructure/MigrationTests.cs
infrastructure/docker-compose.dev.yml
.env.example
.gitignore
```

## Notes for Executor
- AiTokenMeteringService: use fire-and-forget — `_ = Task.Run(async () => { ... })` with
  full try/catch logging inside. The calling request must never wait for metering.
- SemanticCacheService: normalise command before hashing — lowercase, trim, collapse whitespace
- ModelResolutionService: capability check happens at ResolveModelConfigAsync — throw
  ConfigViolationException with clear message before any AI call ever happens
- EmailService: use System.Net.Http.HttpClient registered as typed client.
  Resend API endpoint: POST https://api.resend.com/emails
  Headers: Authorization: Bearer {key}, Content-Type: application/json
  Body: { from, to, subject, html, text }
- Health check: use Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore
  and AspNetCore.HealthChecks.Redis packages
- Do NOT use ASP.NET Identity — custom JWT only as per FUNCTIONAL.md §3.1
- Scalar: use Scalar.AspNetCore package, configure at /scalar, always public
