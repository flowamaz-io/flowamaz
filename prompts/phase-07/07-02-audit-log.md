---
prompt-id: 07-02-audit-log
phase: 07
sequence: 2
roles: [Executor, Verifier, Security]
type: feature
depends-on: [07-01-billing-stripe]
estimated-complexity: Medium
---

# Audit Log — Complete Audit Trail for Workspace Operations

## Context
Enterprise customers require a complete audit trail of all significant actions
for compliance (SOC 2, ISO 27001, GDPR). This prompt implements structured audit
logging for all workspace-level operations.

## Objective
Immutable audit log capturing who did what, when, from where, with before/after
state for all significant operations.

## Scope

### What to Build

**AuditEvent entity:**
```
audit_events
  id                uuid pk
  org_id            uuid fk
  workspace_id      uuid fk (nullable — some events are org-level)
  actor_user_id     uuid fk org_users (nullable for system events)
  actor_type        varchar (user|api_key|system|webhook)
  actor_label       varchar (user display name or API key name)
  event_type        varchar (workflow.created, workflow.published, etc)
  resource_type     varchar (workflow|instance|member|connector|api_key|gate|webhook|sso)
  resource_id       uuid
  resource_label    varchar (human-readable name of the resource)
  action            varchar (created|updated|deleted|published|triggered|approved|rejected|invited|revoked)
  metadata          jsonb (before/after state, IP address, user agent)
  ip_address        inet
  user_agent        varchar
  created_at        timestamptz
  
  -- Immutability: no updated_at, no is_deleted, no soft delete
  -- Append-only: no UPDATE or DELETE ever issued on this table
```

**Audit event types to capture:**

Workflows: created, updated, published, archived, deleted, triggered, test_run
Instances: started, completed, failed, cancelled
Gates: delivered, approved, rejected, reverted, timed_out
Members: invited, role_changed, removed
API keys: created, revoked
Connectors: installed, uninstalled, credentials_updated
Webhooks: created, rotated, deleted, triggered
SSO: configured, enabled, disabled, login_succeeded, login_failed
Billing: plan_upgraded, plan_downgraded, payment_failed
Settings: workspace_updated, retention_changed

**IAuditService + AuditService:**

`RecordAsync(AuditEventRequest request, ct)`:
- Fire-and-forget (Task.Run, same pattern as token metering)
- Never throws — errors logged but not propagated
- Stores in audit_events table

`AuditEventRequest`:
- OrgId, WorkspaceId, ActorUserId, ActorType
- EventType, ResourceType, ResourceId, ResourceLabel, Action
- Metadata (anonymous object → JsonElement)
- IpAddress (from HttpContext.Connection.RemoteIpAddress)
- UserAgent (from HttpContext.Request.Headers)

**AuditMiddleware:**
Registers IHttpContextAccessor so AuditService can read IP/UserAgent.
IP extracted correctly behind nginx proxy (X-Forwarded-For header).

**Wire audit events at service layer (not controller):**
- WorkflowService.CreateAsync → audit workflow.created
- WorkflowService.PublishAsync → audit workflow.published
- WorkflowOrchestrator.TriggerAsync → audit instance.started
- GateService.DecideAsync → audit gate.approved/rejected
- WorkspaceMemberService.InviteAsync → audit member.invited
- ApiKeyService.CreateAsync → audit api_key.created
- ApiKeyService.RevokeAsync → audit api_key.revoked
- WebhookService.CreateEndpointAsync → audit webhook.created
- SsoService.LoginAsync → audit sso.login_succeeded/failed
- StripeService (subscription events) → audit billing.plan_upgraded etc

**API endpoints:**

`GET /api/v1/workspaces/{id}/audit` [RequireWorkspaceRole(Admin)]
Query params: ?from=&to=&event_type=&actor_id=&resource_type=&page=&page_size=
Returns: paginated audit events, newest first
Max page size: 100. Max date range: 90 days.

`GET /api/v1/organisations/{id}/audit` [OrgAdmin]
Same as above but org-wide (all workspaces)

`GET /api/v1/workspaces/{id}/audit/export` [OrgAdmin]
Returns CSV download of audit events for the requested period
Max export range: 365 days

**Frontend — Audit Log View:**

`AuditLogView.vue` (/settings/audit):
- Date range picker (last 7 days default)
- Filter by: event type, actor, resource type
- Table: timestamp | actor | action | resource | IP address | [Details]
- [Details] → expandable row showing metadata JSON
- [Export CSV] button
- Pagination

Each row formatted as human-readable:
"Jagan Mohan created workflow 'Fund Pipeline Status update'"
"System triggered instance #abc123 of 'Fund Pipeline Status update'"
"API Key 'CI Pipeline' published workflow 'Purchase Approval'"

Add "Audit Log" to workspace settings navigation.

**Retention policy:**
AuditRetentionJob (daily):
- Community/Starter: delete audit events older than 30 days
- Pro: 90 days
- Enterprise: 365 days
Retention enforced server-side, not configurable by user.

**Unit tests:**
- AuditService: RecordAsync stores event with correct fields
- AuditService: error in storage does not propagate (fire-and-forget)
- AuditService: IP extracted correctly from X-Forwarded-For
- AuditController: non-admin → 403
- AuditController: date range > 90 days → 400

## Technical Requirements
- [ ] audit_events table: append-only, no UPDATE/DELETE ever
- [ ] Fire-and-forget: never blocks the main request
- [ ] IP from X-Forwarded-For (behind nginx proxy)
- [ ] Retention job runs daily, respects plan limits
- [ ] Export CSV: streaming response for large datasets

## Acceptance Criteria
- [ ] Create workflow → appears in audit log
- [ ] Trigger instance → appears in audit log with actor and IP
- [ ] Export CSV → downloads correctly
- [ ] Non-admin → 403 on audit endpoints
- [ ] Events older than retention period → deleted by job

## Output Expected
```
backend/Flowamaz.Core/Entities/Audit/AuditEvent.cs
backend/Flowamaz.Application/Audit/AuditService.cs
backend/Flowamaz.Api/Controllers/AuditController.cs
backend/Flowamaz.Infrastructure/Jobs/AuditRetentionJob.cs
backend/Flowamaz.Tests.Unit/Audit/AuditServiceTests.cs
web/src/views/settings/AuditLogView.vue
```
