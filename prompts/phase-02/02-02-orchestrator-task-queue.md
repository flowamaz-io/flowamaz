---
prompt-id: 02-02-orchestrator-task-queue
phase: 02
sequence: 2
roles: [Executor, Verifier]
type: feature
depends-on: [02-01-workflow-instance-entities]
estimated-complexity: High
---

# Orchestrator + Task Queue — Durable Execution Core

## Context
All workflow entities exist. Now build the execution engine core — the orchestrator
that walks the SFG graph, the task queue that distributes work, and the worker loop
that claims and processes instances. See FUNCTIONAL.md §2.5 (durable execution model)
and §2.6 (Git-native versioning note on instance pinning).

## Objective
Build the durable execution orchestrator with exactly-once guarantees, the Redis-backed
task queue with SKIP LOCKED, and the worker loop with 30s lease renewal.

## Scope

### What to Build

**YAML SFG Parser (Flowamaz.Core/Workflow/SfgParser.cs):**

Parses the FlowAmaz workflow YAML into a typed in-memory graph.
Use YamlDotNet — never raw string manipulation.

WorkflowGraph model:
```csharp
record WorkflowGraph(
    string WorkflowId,
    string Version,
    IReadOnlyList<SfgNode> Nodes,
    IReadOnlyList<SfgEdge> Edges,
    WorkflowMetadata Metadata
);

record SfgNode(
    string Id, NodeType Type, string Label,
    JsonDocument Config,        // node-specific config
    RetryPolicy? RetryPolicy,   // null = no retry
    TimeoutPolicy? TimeoutPolicy,
    CompensationBlock? Compensation
);

record SfgEdge(
    string Id, string FromNodeId, string ToNodeId,
    string? Condition               // null = unconditional
);

record RetryPolicy(int MaxAttempts, int BackoffSeconds, double BackoffMultiplier);
record TimeoutPolicy(int TimeoutSeconds, string? OnTimeoutNodeId);
record CompensationBlock(string Strategy, string? CompensateNodeId);
```

NodeType enum: Trigger, Action, Ai, HumanGate, Router, End,
  ForEach, Parallel, TryCatch, Wait, IfElse, Switch, While, SubWorkflow

SfgParser.ParseAsync(yamlContent) → WorkflowGraph
- Validates: all edge FromNodeId/ToNodeId reference existing nodes
- Validates: exactly one Trigger node
- Validates: at least one End node
- Validates: no orphaned nodes (every non-trigger node reachable from trigger)
- Throws SfgParseException (with line/field detail) on any validation failure
- Unit tests: valid graph, missing trigger, orphaned node, unknown edge target

**Orchestrator (Flowamaz.Application/Workflow/Orchestrator/):**

`IWorkflowOrchestrator` + `WorkflowOrchestrator`:

`TriggerAsync(workspaceId, workflowDefinitionId, payload, idempotencyKey?, ct)`
  → WorkflowInstance
  - Resolves latest published version (or specified version)
  - Creates WorkflowInstance with status=Pending, pins WorkflowVersionId
  - Checks idempotencyKey uniqueness — if duplicate, returns existing instance
  - Appends WorkflowEvent: InstanceStarted
  - Pushes instance ID onto Redis task queue (LPUSH `fmz:queue:pending`)
  - Returns the new instance

`StepAsync(instanceId, leaseId, ct)` → OrchestratorResult
  - Called by worker after claiming an instance
  - Reads instance + current node state + workflow graph (from cached YAML)
  - Determines next node(s) to execute based on completed nodes and graph edges
  - For Router nodes: evaluates conditions against current variables
  - For Parallel nodes: fans out — creates NodeState entries for all branches
  - Appends WorkflowEvent for each node transition
  - Returns: { NextNodes: [...], IsComplete: bool, NeedsHumanGate: bool }

`CompleteNodeAsync(instanceId, nodeId, output, leaseId, ct)`
  - Marks node as Completed, stores output as WorkflowVariable
  - Appends NodeCompleted event
  - Checks if all required predecessor nodes complete → triggers StepAsync

`FailNodeAsync(instanceId, nodeId, error, leaseId, ct)`
  - Marks node as Failed
  - Checks retry policy — if retries remaining: re-queue with backoff delay
  - If no retries: check compensation strategy, begin saga if configured
  - Appends NodeFailed event

`CancelAsync(instanceId, requestedBy, ct)`
  - Sets status=Cancelled, appends InstanceCancelled event
  - Releases worker lease

**Task Queue (Flowamaz.Infrastructure/Queue/):**

`ITaskQueue` + `RedisTaskQueue`:

Uses Redis Lists (LPUSH / BRPOPLPUSH pattern):
- Pending queue: `fmz:queue:{workspaceId}:pending` (LPUSH to add)
- Processing queue: `fmz:queue:{workspaceId}:processing` (BRPOPLPUSH to claim atomically)
- Delayed queue: `fmz:queue:delayed` (sorted set, score = execute_after Unix timestamp)

`EnqueueAsync(workspaceId, instanceId, ct)` — LPUSH
`EnqueueDelayedAsync(workspaceId, instanceId, delaySeconds, ct)` — ZADD with future timestamp
`DequeueAsync(workspaceId, leaseId, ct)` → string? instanceId
  — BRPOPLPUSH pending → processing (atomic, blocks up to 1s)
`AcknowledgeAsync(workspaceId, instanceId, ct)` — LREM from processing
`ReturnToQueueAsync(workspaceId, instanceId, ct)` — move from processing back to pending
`GetDelayedReadyAsync(ct)` → List<(workspaceId, instanceId)>
  — ZRANGEBYSCORE fmz:queue:delayed 0 now — items ready to promote to pending

**Worker Loop (Flowamaz.Infrastructure/Workers/OrchestratorWorker.cs):**

BackgroundService implementation:
```
while not cancelled:
  for each active workspace:
    instanceId = queue.DequeueAsync(workspaceId, leaseId)
    if instanceId == null: continue
    
    claim instance in DB (SKIP LOCKED — already handled by DequeueAsync + DB record)
    start lease renewal Task (every 10s: RenewLeaseAsync)
    
    try:
      result = orchestrator.StepAsync(instanceId, leaseId)
      if result.IsComplete: queue.AcknowledgeAsync
      elif result.NeedsHumanGate: queue.AcknowledgeAsync (gate handles re-queue)
      else: queue.ReturnToQueueAsync (more steps needed)
    catch:
      log error
      orchestrator.FailNodeAsync
      queue.AcknowledgeAsync
    finally:
      cancel lease renewal Task
```

Worker concurrency: configurable via `Worker:Concurrency` (default 4 concurrent instances).
Use SemaphoreSlim to limit concurrent instance processing.

**Delayed queue promoter (Quartz.NET job):**
Job runs every 5 seconds. Calls GetDelayedReadyAsync, promotes ready items to pending queue.
Registered in Program.cs via Quartz.NET scheduler.

**Variable evaluation service:**
`IVariableEvaluationService` + `VariableEvaluationService`:
Evaluates `{{ variable.name }}` expressions in node config against WorkflowVariable table.
Simple string interpolation for Phase 2 — no complex expressions yet.

**Unit tests:**
- SfgParser: valid graph, missing trigger, orphaned node, router condition evaluation
- WorkflowOrchestrator: trigger creates instance + event, idempotency key deduplication,
  StepAsync fan-out for parallel nodes, router branch selection
- RedisTaskQueue: enqueue → dequeue → acknowledge round-trip (Testcontainers Redis)
- OrchestratorWorker: processes one instance end-to-end (in-memory mocks)

## Technical Requirements
- [ ] YamlDotNet for all YAML parsing — no raw string manipulation
- [ ] SfgParser validates graph structure before any execution
- [ ] Idempotency key: duplicate trigger returns existing instance, no new instance created
- [ ] Exactly-once: BRPOPLPUSH for atomic claim + DB SKIP LOCKED for lease
- [ ] Worker lease: 30s duration, renewed every 10s via background Task
- [ ] Parallel fan-out: all branch nodes created in one transaction
- [ ] Delayed queue uses Redis sorted set — score is Unix timestamp
- [ ] Worker concurrency: SemaphoreSlim, default 4, configurable
- [ ] dotnet build — 0 errors, 0 warnings
- [ ] dotnet test — all pass

## Acceptance Criteria
- [ ] SfgParser.ParseAsync: valid YAML → WorkflowGraph with correct node/edge counts
- [ ] SfgParser: orphaned node → SfgParseException with node ID in message
- [ ] TriggerAsync: same idempotency key twice → returns same instance (integration test)
- [ ] RedisTaskQueue: BRPOPLPUSH is atomic — no double-claim under concurrent workers
- [ ] OrchestratorWorker: starts as BackgroundService, logs worker ID on startup
- [ ] Delayed queue promoter: Quartz job registered and starts

## Output Expected
```
backend/Flowamaz.Core/Workflow/SfgParser.cs
backend/Flowamaz.Core/Workflow/WorkflowGraph.cs (graph models)
backend/Flowamaz.Core/Interfaces/Workflow/IWorkflowOrchestrator.cs
backend/Flowamaz.Core/Interfaces/Queue/ITaskQueue.cs
backend/Flowamaz.Core/Interfaces/Services/IVariableEvaluationService.cs
backend/Flowamaz.Application/Workflow/Orchestrator/WorkflowOrchestrator.cs
backend/Flowamaz.Infrastructure/Queue/RedisTaskQueue.cs
backend/Flowamaz.Infrastructure/Workers/OrchestratorWorker.cs
backend/Flowamaz.Infrastructure/Jobs/DelayedQueuePromoterJob.cs
backend/Flowamaz.Infrastructure/Services/VariableEvaluationService.cs
backend/Flowamaz.Tests.Unit/Workflow/SfgParserTests.cs
backend/Flowamaz.Tests.Unit/Workflow/WorkflowOrchestratorTests.cs
backend/Flowamaz.Tests.Integration/Queue/RedisTaskQueueTests.cs
```
