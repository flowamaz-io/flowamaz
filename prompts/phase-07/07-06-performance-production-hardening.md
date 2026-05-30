---
prompt-id: 07-06-performance-production-hardening
phase: 07
sequence: 6
roles: [Executor, Verifier, Security]
type: feature
depends-on: [07-05-onboarding-improvements]
estimated-complexity: Medium
---

# Performance + Production Hardening

## Context
The application is functionally complete. Before launch, this prompt addresses
performance, observability, and production hardening items identified across
phases 1-6 testing.

## Objective
Database index audit, query optimisation, health check improvements,
structured error tracking, and production readiness checklist.

## Scope

### What to Build

**Database index audit:**

Run EXPLAIN ANALYZE on the 10 most frequent query patterns and add missing indexes:

Expected missing indexes (add migration AddProductionIndexes):
```sql
-- Instance queries (most frequent)
CREATE INDEX idx_workflow_instances_workspace_status 
  ON workflow_instances(workspace_id, status) WHERE is_deleted = false;

CREATE INDEX idx_workflow_instances_workspace_created 
  ON workflow_instances(workspace_id, created_at DESC) WHERE is_deleted = false;

-- Audit log queries
CREATE INDEX idx_audit_events_workspace_created
  ON audit_events(workspace_id, created_at DESC);

CREATE INDEX idx_audit_events_org_created
  ON audit_events(org_id, created_at DESC);

-- Workflow events (timeline queries)
CREATE INDEX idx_workflow_events_instance_created
  ON workflow_events(workflow_instance_id, created_at ASC);

-- Notifications
CREATE INDEX idx_notifications_user_read
  ON notifications(user_id, is_read, created_at DESC);

-- AI token usage (analytics)
CREATE INDEX idx_ai_token_usage_workspace_created
  ON ai_token_usage(workspace_id, created_at DESC);
```

**N+1 query fixes:**

Audit all repository methods that load related data:
- WorkflowDefinitionRepository.GetByWorkspaceAsync: ensure connectors not loaded individually
- WorkflowInstanceRepository.GetWithEventsAsync: use Include().ThenInclude() not lazy loading
- InstanceListView API: ensure single query with pagination, not N queries for N instances

**Response caching:**

Add response caching for frequently-read, rarely-changed data:

```csharp
// Connector catalogue (changes only when admin seeds new connectors)
[ResponseCache(Duration = 3600, VaryByHeader = "Authorization")]
GET /api/v1/library/connectors  

// Plan definitions (changes only on Stripe sync)
[ResponseCache(Duration = 86400)]
GET /api/v1/billing/plans

// Help articles (static content)
[ResponseCache(Duration = 3600)]
GET /api/v1/help/articles
```

**Enhanced health checks:**

Extend GET /health to include all dependencies with response times:

```json
{
  "status": "healthy",
  "version": "1.0.0",
  "environment": "Production",
  "totalDurationMs": 23,
  "dependencies": {
    "database": { "status": "healthy", "responseMs": 4 },
    "redis": { "status": "healthy", "responseMs": 1 },
    "stripe": { "status": "healthy", "responseMs": 145 },
    "anthropic": { "status": "healthy", "responseMs": 312 },
    "resend": { "status": "healthy", "responseMs": 89 }
  }
}
```

Stripe health: GET https://status.stripe.com/api/v2/summary.json — check status
Anthropic health: lightweight ping to models endpoint
Resend health: check domain status

**Structured error tracking:**

Add correlation ID to all unhandled exceptions in Serilog:
Already implemented in Phase 1. Verify:
- CorrelationId present in all error logs
- CorrelationId returned in all 500 responses
- Frontend shows CorrelationId in error UI for support

Add error rate alerting via Serilog sink:
If error rate > 10 errors/minute: log a Critical-level alert
(Real alerting — CloudWatch/Datadog — is infrastructure config, not code)

**Frontend performance:**

Lazy load route components (already good practice in Vue 3):
Verify all route components use dynamic imports:
```typescript
const WorkflowEditorView = () => import('./views/workflow/WorkflowEditorView.vue')
```

Add loading skeleton for all data-fetching components:
`FmSkeleton.vue` — animated gray placeholder
Use in: WorkflowListView, InstanceListView, DashboardView metric cards

**Production checklist enforcement:**

Add startup validation for all required environment variables (extend existing):
```csharp
// In Program.cs startup validation:
var required = new[] {
  "DB_CONNECTION_STRING", "JWT_SECRET", "CREDENTIAL_MASTER_KEY",
  "GATE_SIGNING_KEY", "Email__ResendApiKey", "Ai__AnthropicPlatformKey"
};
// Already partially done — verify all 6 are checked in non-Development
```

Add to DEPLOYMENT.md: complete production environment variable checklist
with description of each variable and how to generate it.

**Unit tests:**
- HealthCheckService: all dependencies healthy → 200 with healthy status
- HealthCheckService: database down → 503 with degraded status
- HealthCheckService: Redis down → 503 with degraded status
- ResponseCaching: connector list cached for 1 hour

## Technical Requirements
- [ ] 7 new database indexes applied via EF Core migration
- [ ] Health check includes all 5 external dependencies
- [ ] Route components lazy-loaded in Vue router
- [ ] Skeleton loading on WorkflowListView and InstanceListView
- [ ] All 6 required env vars validated at startup

## Acceptance Criteria
- [ ] GET /health returns all 5 dependency statuses with response times
- [ ] EXPLAIN ANALYZE on instance list query shows index scan (not seq scan)
- [ ] WorkflowListView shows skeleton while loading
- [ ] Missing required env var at startup → clear error message in logs

## Output Expected
```
backend/Flowamaz.Infrastructure/Migrations/AddProductionIndexes.cs
backend/Flowamaz.Application/Health/HealthCheckService.cs (extended)
backend/Flowamaz.Tests.Unit/Health/HealthCheckServiceTests.cs
web/src/components/shared/FmSkeleton.vue
web/src/router/index.ts (lazy loading verified)
docs/DEPLOYMENT.md (production checklist complete)
```
