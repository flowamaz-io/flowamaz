---
prompt-id: 02-01-workflow-instance-entities
phase: 02
sequence: 1
roles: [Executor, Verifier, Security]
type: feature
depends-on: []
estimated-complexity: High
---

# Workflow & Instance Entities — Core Domain Model + Event Log + Startup Migration

## Context
Phase 1 complete. All foundation entities (Organisation, Workspace, RBAC, Auth) exist.
Phase 2 builds the workflow execution engine. This prompt creates the core domain
entities that everything else in Phase 2 depends on. Read FUNCTIONAL.md §2 (architecture)
and §6.1 (workflow creation methods) before starting.

Travel-forward item from fix-01: add startup DB migration so Docker self-bootstraps.

## Objective
Create all workflow and instance entities, the append-only event log, migrations,
and a startup migration service so the Docker container applies migrations on boot
without manual intervention.

## Scope

### What to Build

**Entities (Flowamaz.Core/Entities/Workflow/):**

`WorkflowDefinition`:
- Id (Guid), WorkspaceId (FK), Name, Slug (unique per workspace)
- Description (string?), YamlContent (text — the full workflow YAML)
- NlDescription (text? — the plain English description used to generate it)
- CreatedByMethod (enum: NaturalLanguage/Voice/VisualInput/Conversation/Document/Canvas)
- Status (enum: Draft/Published/Archived)
- CurrentVersion (string — Git commit SHA or "draft"), HealthScore (int 0-100, default 100)
- TriggerType (enum: Webhook/Form/Schedule/Manual/SubWorkflow)
- CreatedBy (Guid), CreatedAt, UpdatedAt, IsDeleted, DeletedAt
- WorkspaceId index, (WorkspaceId + Slug) unique index

`WorkflowVersion`:
- Id (Guid), WorkspaceId (FK), WorkflowDefinitionId (FK)
- CommitSha (string), TagName (string?), BranchName (string)
- YamlContent (text — snapshot at this version), Message (string)
- IsProduction (bool), CreatedBy (Guid), CreatedAt
- Index: (WorkflowDefinitionId + CommitSha) unique

`WorkflowInstance`:
- Id (Guid), WorkspaceId (FK), WorkflowDefinitionId (FK)
- WorkflowVersionId (FK — pinned at trigger time, immutable forever)
- Status (enum: Pending/Running/Waiting/Completed/Failed/Cancelled/Compensating)
- TriggerType (enum: Manual/Webhook/Schedule/SubWorkflow)
- TriggerPayload (jsonb?), CorrelationId (string? — external reference)
- StartedAt (DateTime?), CompletedAt (DateTime?), FailedAt (DateTime?)
- ErrorMessage (string?), CurrentNodeId (string?)
- WorkerLeaseId (string? — which worker owns this instance)
- WorkerLeaseExpiresAt (DateTime? — 30s lease, renewed every 10s)
- IdempotencyKey (string? unique — prevents duplicate triggers)
- SagaState (enum: None/Compensating/CompensationFailed), SagaStrategy (string?)
- CreatedAt, UpdatedAt
- Indexes: WorkspaceId, Status+WorkerLeaseExpiresAt (for queue), IdempotencyKey unique

`WorkflowEvent` (append-only event log — NEVER updated, NEVER deleted):
- Id (Guid), WorkspaceId (FK), InstanceId (FK → WorkflowInstance)
- SequenceNumber (long — monotonically increasing per instance)
- EventType (string — "NodeStarted", "NodeCompleted", "NodeFailed",
  "InstanceStarted", "InstanceCompleted", "InstanceFailed",
  "GateOpened", "GateDecided", "CompensationStarted", "CompensationCompleted")
- NodeId (string?), NodeType (string?)
- Payload (jsonb — inputs, outputs, error details)
- OccurredAt (DateTime — UTC, precise)
- CreatedAt (DateTime — when written to DB)
- Indexes: (InstanceId + SequenceNumber) unique, (InstanceId + OccurredAt),
  WorkspaceId for tenant queries
- NO soft delete — this table is immutable audit log
- NO update operations — insert only

`WorkflowNodeState` (current state snapshot per node — mutable, for fast reads):
- Id (Guid), WorkspaceId (FK), InstanceId (FK)
- NodeId (string), NodeType (string)
- Status (enum: Pending/Running/Completed/Failed/Skipped/Compensating)
- StartedAt (DateTime?), CompletedAt (DateTime?)
- InputPayload (jsonb?), OutputPayload (jsonb?)
- ErrorMessage (string?), RetryCount (int default 0), LastRetryAt (DateTime?)
- Index: (InstanceId + NodeId) unique

`WorkflowVariable` (runtime variables for an instance):
- Id (Guid), WorkspaceId (FK), InstanceId (FK)
- Name (string), Value (jsonb), IsSensitive (bool — if true, value encrypted)
- UpdatedAt (DateTime)
- Index: (InstanceId + Name) unique

`GateDecision` (human approval decisions):
- Id (Guid), WorkspaceId (FK), InstanceId (FK), NodeId (string)
- AssignedTo (Guid? — OrgUser), AssignedToEmail (string?)
- Decision (enum: Pending/Approved/Rejected/Escalated)
- DecidedBy (Guid?), DecidedAt (DateTime?)
- DecisionNote (string?), DeliveryChannel (enum: Slack/Teams/Email/Portal)
- DeliveryStatus (enum: Pending/Sent/Failed), DeliveryError (string?)
- ExpiresAt (DateTime?), EscalatedTo (Guid?)
- CreatedAt, UpdatedAt
- Index: (InstanceId + NodeId) unique, AssignedTo, ExpiresAt (for timer scanner)

**Migration: `AddWorkflowSchema`**

**Enums to add (Flowamaz.Core/Enums/):**
WorkflowStatus, WorkflowCreatedByMethod, WorkflowTriggerType,
InstanceStatus, InstanceTriggerType, SagaState,
NodeStatus, GateDecision, GateDeliveryChannel, GateDeliveryStatus

**Startup Migration Service:**

`IStartupMigrationService` + `StartupMigrationService`:
- RunAsync(CancellationToken) — applies all pending EF Core migrations on app startup
- Logs: "Applying N pending migrations..." → migration names → "Migrations complete"
- On failure: logs error with full exception, re-throws (app should not start with bad schema)
- Timeout: 2 minutes (configurable via StartupMigrationTimeoutSeconds in appsettings)

Register in Program.cs after all services, before app.Run():
```csharp
using (var scope = app.Services.CreateScope())
{
    var migrationService = scope.ServiceProvider
        .GetRequiredService<IStartupMigrationService>();
    await migrationService.RunAsync(cancellationToken);
}
```

This means `docker compose up` brings up the DB, backend starts, migrations apply
automatically, then the app begins serving. No manual `dotnet ef database update`.

**Repositories:**
- IWorkflowDefinitionRepository + WorkflowDefinitionRepository
- IWorkflowVersionRepository + WorkflowVersionRepository
- IWorkflowInstanceRepository + WorkflowInstanceRepository
  - Special methods: GetPendingForWorkerAsync(leaseId, batchSize, ct)
    — claims instances using SKIP LOCKED (raw SQL for this one operation)
    — UPDATE SET worker_lease_id = @leaseId, worker_lease_expires_at = @expiry
      WHERE status = 'Pending' AND (worker_lease_expires_at IS NULL OR worker_lease_expires_at < NOW())
      RETURNING *
  - RenewLeaseAsync(instanceId, leaseId, ct)
  - ReleaseLeaseAsync(instanceId, leaseId, ct)
- IWorkflowEventRepository + WorkflowEventRepository
  - AppendAsync(event) — insert only, never update
  - GetForInstanceAsync(instanceId, ct) → List<WorkflowEvent> ordered by SequenceNumber
  - GetNextSequenceNumberAsync(instanceId, ct) → long
- IGateDecisionRepository + GateDecisionRepository

**Unit tests:**
- WorkflowInstance status transition tests (valid and invalid transitions)
- WorkflowEvent sequence number increment test
- Startup migration service: already-applied → no-op, pending → applies, DB down → throws

### What NOT to Build
- No workflow execution logic — prompts 02-03
- No API endpoints — prompt 04
- No YAML parsing — prompt 03
- No worker processes — prompts 02-03

## Technical Requirements
- [ ] WorkflowEvent table has NO soft delete, NO update — insert only enforced at repository
- [ ] WorkflowInstance.IdempotencyKey has unique index — duplicate triggers caught at DB
- [ ] WorkerLease: 30s duration, stored in instance record
- [ ] StartupMigrationService: runs before app.Run(), logs each migration name
- [ ] StartupMigrationService: timeout 2 minutes, re-throws on failure
- [ ] SKIP LOCKED query uses raw SQL (EF Core cannot express this)
- [ ] All workspace-scoped entities filter by WorkspaceId
- [ ] dotnet build — 0 errors, 0 warnings
- [ ] dotnet test — all pass including startup migration tests

## Acceptance Criteria
- [ ] Migration applies cleanly on fresh Postgres
- [ ] WorkflowEvent: AppendAsync works, no update/delete methods exist on repository
- [ ] WorkflowInstance: SKIP LOCKED claim works (integration test with Testcontainers)
- [ ] StartupMigrationService: docker compose up applies migrations automatically
- [ ] IdempotencyKey unique constraint: duplicate insert throws (integration test)

## Output Expected
```
backend/Flowamaz.Core/Entities/Workflow/ (all 7 entities)
backend/Flowamaz.Core/Enums/ (all new enums)
backend/Flowamaz.Core/Interfaces/Repositories/ (all new repository interfaces)
backend/Flowamaz.Core/Interfaces/Services/IStartupMigrationService.cs
backend/Flowamaz.Infrastructure/Persistence/Repositories/ (all new repositories)
backend/Flowamaz.Infrastructure/Persistence/Configurations/ (EF configs)
backend/Flowamaz.Infrastructure/Persistence/Migrations/[ts]_AddWorkflowSchema.cs
backend/Flowamaz.Infrastructure/Services/StartupMigrationService.cs
backend/Flowamaz.Tests.Unit/Workflow/WorkflowInstanceTests.cs
backend/Flowamaz.Tests.Unit/Workflow/WorkflowEventTests.cs
backend/Flowamaz.Tests.Unit/Infra/StartupMigrationServiceTests.cs
backend/Flowamaz.Tests.Integration/Workflow/WorkerLeaseTests.cs
```
