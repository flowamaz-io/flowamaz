---
prompt-id: 06-07-phase6-integration
phase: 06
sequence: 7
roles: [Executor, Verifier, Security, Testing]
type: feature
depends-on: [06-06-sidebar-design-final]
estimated-complexity: Medium
---

# Phase 6 Integration — E2E Tests, Security Scan, PHASE_COMPLETE

## Context
All Phase 6 components built. Final prompt: integration tests, Playwright E2E,
security scan, coverage verification, PHASE_COMPLETE.

## Objective
Phase 6 lifecycle tests, E2E for new flows, security scan, PHASE_COMPLETE.

## Scope

### What to Build

**Backend integration tests (Phase6/):**

Phase6WebhookTests.cs:
- CreateEndpoint → endpointId + secret returned
- POST /webhooks/{id} with correct HMAC → 202 + instanceId
- POST /webhooks/{id} with wrong HMAC → 401
- Idempotency key: same key twice → same instanceId

Phase6PublicApiTests.cs:
- GET /api/public/v1/workflows with API key → 200, snake_case response
- POST /api/public/v1/workflows/{slug}/trigger → 202
- Missing API key → 401 with error.code = "unauthorized"
- Rate limit exceeded → 429 with X-RateLimit-Reset header

Phase6SsoTests.cs:
- ProvisionUserFromSso: new user → created with Viewer role
- ProvisionUserFromSso: existing user → updated, not duplicated
- OIDC state mismatch → 401

Phase6NotificationTests.cs:
- Instance complete → notification created for creator
- Gate pending → notification created for assignee
- Mark all read → unread count = 0

**Playwright E2E (add to suite):**

webhook.spec.ts:
- S49: Create webhook endpoint → URL shown with curl example
- S50: POST to webhook → instance created (mock HMAC in test)

public-api.spec.ts:
- S51: /api-docs → Scalar renders public API docs
- S52: GET /api/public/v1/workflows with API key → returns workflows

notifications.spec.ts:
- S53: Bell badge shows correct unread count
- S54: Click bell → dropdown shows notifications
- S55: Mark all read → badge disappears

connector-detail.spec.ts:
- S56: Click connector card → detail page renders
- S57: Install connector → install count increments

node-config.spec.ts:
- S58: Double-click node → config panel opens
- S59: Configure human-gate → saved in YAML

misc.spec.ts:
- S60: Canvas title shows workflow name (not "Untitled Workflow")

Total E2E: 60 scenarios (S1-S48 existing + S49-S60 Phase 6)

**Security scan focus areas:**

- Webhook HMAC: verify constant-time comparison (no timing attack)
- SSO: verify SAML signature validation, OIDC state parameter check
- Public API: verify API key cannot access other workspace data
- Node config: verify config values are sanitised (no XSS in connector config)
- Community connector submission: verify manifest YAML cannot contain RCE payloads

**Coverage check (Phase 6 areas):**
- WebhookService ≥ 80%
- SsoService ≥ 80%
- NotificationService ≥ 80%
- ConnectorMarketplaceService ≥ 80%
- PublicApiControllers ≥ 80%

**vue/no-v-html fix:**
In FmInterpreterPanel.vue — the pre-existing lint warning from fix-05:
Either add eslint-disable comment with justification (interpreter output is trusted,
sanitized server-side) or replace v-html with a safe markdown renderer.

**Final checkpoint:**
```
Phase: 06
Next Prompt: PHASE_COMPLETE
Completed: 7
Phase End Status: PHASE_COMPLETE
```

## Technical Requirements
- [ ] Phase6 integration tests: all pass
- [ ] S49-S60: all 12 new Playwright scenarios authored
- [ ] Security: HMAC constant-time, OIDC state, SAML signature all verified
- [ ] All Phase 6 service areas ≥ 80% coverage
- [ ] vue/no-v-html warning resolved

## Acceptance Criteria
- [ ] Webhook HMAC: tampered body → 401 (integration test proves)
- [ ] SSO: new user provisioned with Viewer role (integration test proves)
- [ ] Public API: API key from workspace A cannot access workspace B data
- [ ] S49-S60: all authored and passing in CI
- [ ] Total test count: unit ≥ 480, integration ≥ 96

## Output Expected
```
backend/Flowamaz.Tests.Integration/Phase6/Phase6WebhookTests.cs
backend/Flowamaz.Tests.Integration/Phase6/Phase6PublicApiTests.cs
backend/Flowamaz.Tests.Integration/Phase6/Phase6SsoTests.cs
backend/Flowamaz.Tests.Integration/Phase6/Phase6NotificationTests.cs
web/e2e/webhook.spec.ts
web/e2e/public-api.spec.ts
web/e2e/notifications.spec.ts
web/e2e/connector-detail.spec.ts
web/e2e/node-config.spec.ts
web/e2e/misc.spec.ts
checkpoint.md (PHASE_COMPLETE)
results.md (all 7 prompts logged)
```
