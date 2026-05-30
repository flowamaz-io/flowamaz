---
prompt-id: 06-03-public-api
phase: 06
sequence: 3
roles: [Executor, Verifier, Security]
type: feature
depends-on: [06-02-node-config-editor]
estimated-complexity: Medium
---

# Public API — Documented REST API for External Integrations

## Context
Workspace API keys exist (Phase 1) but the public API surface is not documented
or versioned separately from the internal frontend API. This prompt creates a clean
public API with its own versioning, documentation, and rate limiting appropriate
for external developer use.

## Objective
Clean public API at /api/public/v1/ with full OpenAPI documentation, separate rate limits,
and developer-friendly responses. All endpoints usable with workspace API keys.

## Scope

### What to Build

**Public API endpoints (all under /api/public/v1/):**

Workflows:
- GET /workflows — list published workflows (paginated)
- GET /workflows/{slug} — get workflow by slug
- POST /workflows/{slug}/trigger — trigger a run
- GET /workflows/{slug}/instances — list instances for this workflow

Instances:
- GET /instances/{id} — get instance status and timeline
- GET /instances/{id}/events — stream instance events (SSE)
- POST /instances/{id}/cancel — cancel running instance

Gates:
- GET /gates/pending — list pending gates for workspace
- POST /gates/{id}/decide — approve/reject a gate

**Authentication:**
All public API endpoints use `Authorization: Bearer {api_key}` where api_key is a
workspace API key (already exists in Phase 1). No JWT cookies for public API.

Rate limits (separate from internal API):
- 1000 requests/hour per API key (burst: 100/min)
- /trigger endpoints: 100/hour per API key
- Headers: X-RateLimit-Limit, X-RateLimit-Remaining, X-RateLimit-Reset

**Response format (public API — NOT AutoWrapper):**
The public API uses a clean JSON response format without AutoWrapper envelope:

Success:
```json
{
  "data": { ... },
  "meta": { "page": 1, "per_page": 20, "total": 45 }
}
```

Error:
```json
{
  "error": {
    "code": "workflow_not_found",
    "message": "No published workflow found with slug 'my-workflow'",
    "docs": "https://docs.flowamaz.io/api/errors#workflow_not_found"
  }
}
```

Note: snake_case field names for public API (industry standard).

**OpenAPI documentation:**
Register a second Scalar endpoint at /api-docs (separate from /scalar):
- Title: "Flowamaz Public API"
- Description: "REST API for triggering workflows and reading instances"
- Auth: API key in Authorization header
- All public endpoints visible, internal /api/v1/ endpoints hidden

**PublicApiController base class:**
Handles API key auth, rate limit headers, snake_case serialization.
All public API controllers inherit from this.

**API key middleware extension:**
Existing WorkspaceApiKeyMiddleware already handles API key auth.
Extend it to also set rate limit headers on public API routes.

**SDK hint responses:**
Every 401 response includes:
```json
{
  "error": {
    "code": "unauthorized",
    "message": "API key required. Generate one at app.flowamaz.io/settings/api-keys",
    "docs": "https://docs.flowamaz.io/authentication"
  }
}
```

**Unit tests:**
- PublicApiWorkflowsController: valid API key → returns published workflows
- PublicApiWorkflowsController: trigger with invalid slug → 404 with error body
- PublicApiInstancesController: get instance → correct snake_case response
- RateLimitService: exceeded limit → 429 with correct headers
- PublicApiController: missing API key → 401 with SDK hint

## Technical Requirements
- [ ] /api/public/v1/ prefix distinct from /api/v1/ (internal)
- [ ] No AutoWrapper — custom response format with snake_case
- [ ] Rate limit headers on every response
- [ ] Second Scalar instance at /api-docs (public endpoints only)
- [ ] API key auth only — no JWT cookie auth on public routes
- [ ] Idempotency key support on /trigger (same as webhook)

## Acceptance Criteria
- [ ] GET /api/public/v1/workflows with API key → returns published workflows
- [ ] POST /api/public/v1/workflows/{slug}/trigger → 202 with instanceId
- [ ] Missing API key → 401 with SDK hint message
- [ ] Rate limit exceeded → 429 with X-RateLimit-Reset header
- [ ] /api-docs → Scalar shows only public endpoints

## Output Expected
```
backend/Flowamaz.Api/Controllers/Public/PublicApiController.cs
backend/Flowamaz.Api/Controllers/Public/PublicWorkflowsController.cs
backend/Flowamaz.Api/Controllers/Public/PublicInstancesController.cs
backend/Flowamaz.Api/Controllers/Public/PublicGatesController.cs
backend/Flowamaz.Tests.Unit/Public/PublicWorkflowsControllerTests.cs
backend/Flowamaz.Tests.Unit/Public/PublicInstancesControllerTests.cs
```
