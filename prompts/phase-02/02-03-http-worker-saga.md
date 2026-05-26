---
prompt-id: 02-03-http-worker-saga
phase: 02
sequence: 3
roles: [Executor, Verifier, Security]
type: feature
depends-on: [02-02-orchestrator-task-queue]
estimated-complexity: High
---

# HTTP Action Worker + Saga Compensation Engine

## Context
Orchestrator and task queue exist. Now build the HTTP action worker (executes Action
nodes that call external APIs) and the saga compensation engine (handles failure
recovery). See FUNCTIONAL.md §2.7 (saga compensation — Backward/Forward/Pivot).

## Objective
HTTP worker with retry/timeout/idempotency, saga engine with all 3 strategies,
timer scanner for lease expiry and gate timeouts.

## Scope

### What to Build

**HTTP Action Worker (Flowamaz.Application/Workflow/Workers/HttpActionWorker.cs):**

`INodeWorker` interface:
```csharp
interface INodeWorker {
    NodeType SupportedType { get; }
    Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken ct);
}

record NodeExecutionContext(
    Guid InstanceId, Guid WorkspaceId,
    SfgNode Node, WorkflowGraph Graph,
    IReadOnlyDictionary<string, JsonElement> Variables,
    string LeaseId
);

record NodeExecutionResult(
    bool Success, JsonDocument? Output,
    string? ErrorMessage, bool ShouldRetry
);
```

`HttpActionWorker : INodeWorker` (SupportedType = Action):
- Reads connector config from node.Config
- Resolves credential from WorkspaceCredential (Phase 4 — for now: read from node config directly)
- Builds HTTP request from node config (method, url, headers, body template)
- Variable substitution in URL and body using IVariableEvaluationService
- Adds idempotency header: `X-Idempotency-Key: {instanceId}-{nodeId}`
- Executes with timeout from TimeoutPolicy
- Retry on transient errors (5xx, timeout, network) per RetryPolicy
- Exponential backoff between retries
- On success: parse response, store as node output
- On failure after all retries: return ShouldRetry=false

`NodeWorkerRegistry`:
- Registers all INodeWorker implementations by NodeType
- Resolves the correct worker for a given node

**Saga Compensation Engine (Flowamaz.Application/Workflow/Saga/):**

`ISagaEngine` + `SagaEngine`:

Three strategies from FUNCTIONAL.md §2.7:

`Backward` (undo all completed nodes in reverse order):
- Walk completed nodes in reverse execution order
- For each node with a compensation block: execute compensate node
- If compensation fails: log, continue with remaining compensations
- Final state: CompensationCompleted or CompensationFailed

`Forward` (retry the failed node and continue):
- Re-queue the failed node with fresh retry count
- If it succeeds: continue normal execution
- If it fails again: escalate to Backward

`Pivot` (backward before the failed node, forward after):
- Compensate nodes before the pivot point (Backward)
- Retry the pivot node (Forward)
- Most complex — used when partial completion is acceptable

`SagaEngine.StartAsync(instanceId, failedNodeId, strategy, ct)`:
- Sets instance.SagaState = Compensating
- Appends CompensationStarted event
- Executes chosen strategy
- Appends CompensationCompleted or CompensationFailed event
- Updates instance.Status accordingly

**Timer Scanner (Quartz.NET job — Flowamaz.Infrastructure/Jobs/):**

`WorkerLeaseExpiryJob` (runs every 15 seconds):
- Finds instances where WorkerLeaseExpiresAt < UtcNow AND Status = Running
- These are orphaned instances (worker died without releasing lease)
- Re-queues them for pickup by a new worker
- Logs each recovered instance

`GateTimeoutJob` (runs every 60 seconds):
- Finds GateDecision records where ExpiresAt < UtcNow AND Decision = Pending
- For each: sets Decision = Escalated, triggers escalation logic
- If EscalatedTo is set: sends notification to escalation target
- If no escalation: fails the gate node → triggers saga

Both jobs registered in Program.cs via Quartz.NET.

**Unit tests:**
- HttpActionWorker: success path, retry on 503, no retry on 400, timeout respected
- SagaEngine: backward strategy compensates in reverse order, forward re-queues node
- WorkerLeaseExpiryJob: orphaned instance detected and re-queued

## Technical Requirements
- [ ] HttpActionWorker uses HttpClientFactory — not new HttpClient()
- [ ] Idempotency header on every outbound HTTP call
- [ ] Timeout enforced via CancellationTokenSource linked to TimeoutPolicy.TimeoutSeconds
- [ ] Retry uses Polly or manual exponential backoff — no Thread.Sleep
- [ ] SagaEngine: compensation failures do not throw — logged and continued
- [ ] Timer jobs registered with Quartz.NET and start on app boot
- [ ] All workers implement INodeWorker — registered in NodeWorkerRegistry

## Acceptance Criteria
- [ ] HttpActionWorker: 503 response → retries up to MaxAttempts with backoff
- [ ] HttpActionWorker: timeout → NodeExecutionResult with ShouldRetry=false
- [ ] SagaEngine backward: compensates C→B→A for A→B→C execution order
- [ ] WorkerLeaseExpiryJob: orphaned instance (lease expired) → re-queued
- [ ] GateTimeoutJob: expired pending gate → Decision=Escalated event appended

## Output Expected
```
backend/Flowamaz.Core/Interfaces/Workflow/INodeWorker.cs
backend/Flowamaz.Core/Interfaces/Workflow/ISagaEngine.cs
backend/Flowamaz.Application/Workflow/Workers/HttpActionWorker.cs
backend/Flowamaz.Application/Workflow/Workers/NodeWorkerRegistry.cs
backend/Flowamaz.Application/Workflow/Saga/SagaEngine.cs
backend/Flowamaz.Infrastructure/Jobs/WorkerLeaseExpiryJob.cs
backend/Flowamaz.Infrastructure/Jobs/GateTimeoutJob.cs
backend/Flowamaz.Tests.Unit/Workflow/HttpActionWorkerTests.cs
backend/Flowamaz.Tests.Unit/Workflow/SagaEngineTests.cs
backend/Flowamaz.Tests.Unit/Jobs/WorkerLeaseExpiryJobTests.cs
```
