---
prompt-id: 06-01-webhook-triggers
phase: 06
sequence: 1
roles: [Executor, Verifier, Security]
type: feature
depends-on: []
estimated-complexity: High
---

# Webhook Triggers — Per-Workflow Webhook Endpoints

## Context
Phases 1-5 complete. Workflows exist with trigger type "webhook" in YAML but
no actual HTTP endpoint receives the webhook payload. This prompt implements
real webhook endpoints — each workspace gets a unique webhook URL, each workflow
has its own path, and incoming requests are verified and trigger instances.

## Objective
Real webhook trigger endpoints with HMAC signature verification, payload validation,
idempotency keys, and synchronous/asynchronous response modes.

## Scope

### What to Build

**WebhookEndpoint entity:**
```
webhook_endpoints
  id                uuid pk
  workspace_id      uuid fk workspaces
  workflow_id       uuid fk workflow_definitions
  secret            varchar (HMAC secret, AES-encrypted at rest)
  is_active         boolean default true
  description       varchar(255)
  allowed_ips       text[] (optional IP allowlist)
  created_at, updated_at, created_by
```

**IWebhookService + WebhookService:**

`CreateEndpointAsync(workspaceId, workflowId, description, ct)`:
- Generate cryptographically random secret (32 bytes hex)
- Encrypt secret with CREDENTIAL_MASTER_KEY (same as vault)
- Store WebhookEndpoint
- Return endpoint with plain secret (shown once — never again)

`GetEndpointAsync(endpointId, workspaceId, ct)` → WebhookEndpointDto (no secret)

`ListEndpointsAsync(workspaceId, ct)` → List<WebhookEndpointDto>

`DeleteEndpointAsync(endpointId, workspaceId, ct)`

`RotateSecretAsync(endpointId, workspaceId, ct)` → new plain secret

`ValidateSignatureAsync(endpointId, rawBody, signatureHeader, ct)` → bool
- Compute HMAC-SHA256 of raw request body using decrypted secret
- Compare with X-Flowamaz-Signature header (or X-Hub-Signature-256 for GitHub compat)
- Constant-time comparison (no timing attack)

`TriggerFromWebhookAsync(endpointId, payload, idempotencyKey, ct)`:
- Validate signature
- Check idempotency key (Redis: "webhook:idem:{key}" TTL 24h)
- Check workflow is published
- Call WorkflowOrchestrator.TriggerAsync with IsTest=false
- Return { instanceId, status: "accepted" }

**API endpoints:**

`POST /api/v1/workspaces/{id}/webhooks`
[RequireWorkspaceRole(Admin)]
Creates webhook endpoint. Returns endpointId + secret (one time).

`GET /api/v1/workspaces/{id}/webhooks`
[RequireWorkspaceRole(Viewer)]

`DELETE /api/v1/workspaces/{id}/webhooks/{endpointId}`
[RequireWorkspaceRole(Admin)]

`POST /api/v1/workspaces/{id}/webhooks/{endpointId}/rotate`
[RequireWorkspaceRole(Admin)]

**Webhook receive endpoint (public — no JWT auth):**
`POST /webhooks/{endpointId}`
[AllowAnonymous]
Rate limited: 100/min per endpoint (Redis counter)

Request handling:
1. Read raw body (before model binding — needed for HMAC)
2. Validate X-Flowamaz-Signature header
3. Parse body as JSON
4. Extract idempotency key from X-Idempotency-Key header (optional)
5. Call WebhookService.TriggerFromWebhookAsync
6. Return 202 Accepted { instanceId } or 200 with instance result (if sync mode)

Sync mode: ?sync=true query param — waits up to 30 seconds for instance to reach
terminal state, returns instance result. Uses long-polling on instance status.

**Frontend — Webhook Management:**

`WebhookSettingsView.vue` (under workspace settings):
- List of webhook endpoints for the workspace
- Each row: workflow name, description, endpoint URL, created date, [Rotate] [Delete]
- [+ Create webhook] button → modal:
  - Select workflow (dropdown of published workflows)
  - Description field
  - Submit → shows created webhook URL + secret with "Copy" button
  - Warning: "This secret will not be shown again"
- Endpoint URL format: `https://app.flowamaz.io/webhooks/{endpointId}`

Add "Webhooks" to workspace settings sidebar navigation.

**Update WorkflowDetailView:**
Add "Trigger via Webhook" section in Overview tab if workflow has webhook endpoints:
- Shows the webhook URL
- Shows curl example
- [Manage webhooks] link to settings

**Unit tests:**
- WebhookService: CreateEndpoint stores encrypted secret
- WebhookService: ValidateSignature correct → true
- WebhookService: ValidateSignature tampered body → false
- WebhookService: TriggerFromWebhook idempotency key deduplicates
- WebhookController: missing signature → 401
- WebhookController: invalid signature → 401
- WebhookController: valid signature + published workflow → 202

## Technical Requirements
- [ ] Raw body read before model binding — use EnableBuffering() middleware
- [ ] HMAC-SHA256 constant-time comparison (CryptographicOperations.FixedTimeEquals)
- [ ] Secret encrypted at rest with CREDENTIAL_MASTER_KEY
- [ ] Secret shown once on creation only
- [ ] Idempotency key deduplication via Redis (24h TTL)
- [ ] Rate limit: 100/min per endpointId (not per IP — webhook senders may have dynamic IPs)
- [ ] /webhooks/* route excluded from JWT middleware
- [ ] Sync mode: max 30s wait, returns 408 if instance not terminal

## Acceptance Criteria
- [ ] Create webhook → get URL + secret
- [ ] POST to webhook URL with correct signature → 202 + instanceId
- [ ] POST with wrong signature → 401
- [ ] Same idempotency key twice → same instanceId returned (no duplicate instance)
- [ ] Frontend shows webhook URL with curl example

## Output Expected
```
backend/Flowamaz.Core/Entities/Webhooks/WebhookEndpoint.cs
backend/Flowamaz.Application/Webhooks/WebhookService.cs
backend/Flowamaz.Api/Controllers/WebhooksController.cs
backend/Flowamaz.Api/Controllers/WebhookReceiveController.cs
backend/Flowamaz.Tests.Unit/Webhooks/WebhookServiceTests.cs
web/src/views/settings/WebhookSettingsView.vue
web/src/components/settings/CreateWebhookModal.vue
```
