---
prompt-id: 02-04-instance-api
phase: 02
sequence: 4
roles: [Executor, Verifier, Security]
type: feature
depends-on: [02-03-http-worker-saga]
estimated-complexity: Medium
---

# Workflow Definition + Instance REST API

## Context
Engine is running. Now expose workflow CRUD and instance management as REST endpoints.
These are the APIs the frontend canvas (Phase 3) and fmz CLI (Phase 5) will use.

## Objective
Full REST API for workflow definitions and instances including trigger, status,
cancel, retry, and live WebSocket status updates.

## Scope

### What to Build

**WorkflowDefinitionsController (/api/v1/workspaces/{workspaceId}/workflows):**
- GET / [Viewer] — paginated list (id, name, slug, status, health_score, updated_at)
- POST / [Designer] — create definition (name, slug, yaml_content, nl_description?, created_by_method)
  — Validates YAML via SfgParser before saving
  — Returns 422 with SfgParseException details on invalid YAML
- GET /{id} [Viewer] — full definition including yaml_content
- PUT /{id} [Designer] — update yaml_content (validates before saving)
- DELETE /{id} [WorkspaceAdmin] — soft delete (cannot delete if active instances exist → 409)
- GET /{id}/versions [Viewer] — list versions ordered by created_at desc
- POST /{id}/publish [Designer] — creates a WorkflowVersion (snapshot + commit SHA)

**WorkflowInstancesController (/api/v1/workspaces/{workspaceId}/instances):**
- GET / [Operator] — paginated list, filterable by status/workflow/date range
- POST / [Operator] — trigger instance
  Body: { workflow_definition_id, payload?, idempotency_key? }
  Returns: { instance_id, status, triggered_at }
- GET /{id} [Operator] — instance detail
  Returns: full instance with node_states[], variables (sensitive masked), events (last 50)
- POST /{id}/cancel [Operator] — cancel running instance
- POST /{id}/retry [Operator] — retry failed instance (creates new instance from same version)
- GET /{id}/events [Operator] — paginated event log (full history)
- GET /{id}/variables [Operator] — current variables (sensitive values masked as "***")
- GET /{id}/timeline [Viewer] — run timeline (see Workflow Interpreter prompt)

**WorkflowGatesController (/api/v1/workspaces/{workspaceId}/gates):**
- GET / [Operator] — list pending gates for workspace (for the portal approval UI)
- GET /{instanceId}/{nodeId} [Operator] — gate detail
- POST /{instanceId}/{nodeId}/decide [Operator]
  Body: { decision: "approved"|"rejected", note? }
  — Records GateDecision, resumes or fails the instance
  — Appends GateDecided event

**WebSocket endpoint (/ws/v1/workspaces/{workspaceId}/instances/{id}):**
- Auth: JWT token as query param ?token=... (standard WebSocket auth pattern)
- On connect: send current instance status snapshot
- On instance status change: push { event_type, status, node_id?, timestamp }
- On disconnect: clean up subscription
- Implementation: use System.Net.WebSockets (no SignalR dependency)
- Subscriptions stored in ConcurrentDictionary<string, WebSocket> scoped per instance

**Request/Response DTOs** (with FluentValidation validators):
- CreateWorkflowDefinitionRequest — name, slug, yaml_content required
- UpdateWorkflowDefinitionRequest — yaml_content required
- TriggerInstanceRequest — workflow_definition_id required, payload optional
- GateDecisionRequest — decision required (enum validated)

**Integration tests:**
- Full workflow lifecycle: create → validate YAML → publish → trigger → poll status
- Invalid YAML → 422 with parse error details
- Duplicate idempotency key → same instance returned
- Cancel running instance → status=Cancelled
- Gate decision: approve → instance resumes, reject → instance fails

## Technical Requirements
- [ ] SfgParser validates YAML before any create/update saves to DB
- [ ] Instance trigger returns 200 with existing instance on duplicate idempotency key
- [ ] Sensitive variables masked as "***" in all API responses
- [ ] WebSocket: JWT token validated before connection accepted
- [ ] WebSocket: max 100 concurrent connections per workspace (configurable)
- [ ] All endpoints return pagination metadata on list responses
- [ ] Soft-deleted workflows return 404 (not 410)

## Acceptance Criteria
- [ ] POST /workflows with invalid YAML → 422 with node/line details
- [ ] POST /instances with same idempotency_key twice → 200 same instance_id
- [ ] POST /instances/{id}/cancel → instance.status = Cancelled in DB
- [ ] GET /instances/{id} → sensitive variables show "***"
- [ ] WebSocket: connect → receive status snapshot → instance progresses → receive update

## Output Expected
```
backend/Flowamaz.Api/Controllers/WorkflowDefinitionsController.cs
backend/Flowamaz.Api/Controllers/WorkflowInstancesController.cs
backend/Flowamaz.Api/Controllers/WorkflowGatesController.cs
backend/Flowamaz.Api/WebSockets/InstanceStatusWebSocketHandler.cs
backend/Flowamaz.Application/Workflow/DTOs/ (all request/response DTOs)
backend/Flowamaz.Application/Workflow/Validators/
backend/Flowamaz.Tests.Integration/Workflow/WorkflowApiTests.cs
backend/Flowamaz.Tests.Integration/Workflow/GateApiTests.cs
```
