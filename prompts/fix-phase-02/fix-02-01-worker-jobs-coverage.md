---
prompt-id: fix-02-01-worker-jobs-coverage
phase: fix-phase-02
sequence: 1
roles: [Executor, Verifier, Testing]
type: fix
source-issue: Testing Agent — Phase 02 report
severity: High
depends-on: []
---

# Fix: Infrastructure/Workers 0% + Infrastructure/Jobs 20% Coverage

## Issue Being Fixed

From phase-02-report.md:

```
ISSUE: Infrastructure/Workers (OrchestratorWorker 0%) and Infrastructure/Jobs (20%)
SEVERITY: High
FINDING: Background components have no unit tests. Integration host disables
the live worker to avoid races, so unit tests with mocked scope are needed.
FIX REQUIRED: Add unit tests for OrchestratorWorker and all Jobs to clear ≥80% gate.
```

## Objective
Write unit tests for OrchestratorWorker, DelayedQueuePromoterJob, WorkerLeaseExpiryJob,
GateTimeoutJob, and ProcessIntelligenceJob using mocked dependencies.
Target: Infrastructure/Workers ≥ 80%, Infrastructure/Jobs ≥ 80%.

## Scope

### What to Build

**OrchestratorWorkerTests (Flowamaz.Tests.Unit/Workers/):**

The worker is a BackgroundService. Test its core processing methods with
IServiceScopeFactory mocked to return mocked services.

Tests to write:
- ProcessNextAsync_NoPendingInstances_ReturnsWithoutProcessing
  Mock ITaskQueue.DequeueAsync → null. Verify orchestrator never called.

- ProcessNextAsync_ClaimsAndProcessesInstance_CompletesSuccessfully
  Mock queue returns instanceId. Mock orchestrator.StepAsync → IsComplete=true.
  Verify queue.AcknowledgeAsync called, lease renewal cancelled.

- ProcessNextAsync_OrchestratorThrows_CallsFailNodeAndAcknowledges
  Mock orchestrator.StepAsync throws. Verify FailNodeAsync called, Acknowledge called,
  error logged, no exception propagated.

- ProcessNextAsync_LeaseRenewalFires_CallsRenewLeaseAsync
  Use a slow orchestrator mock (Task.Delay) + short renewal interval.
  Verify RenewLeaseAsync called at least once during processing.

- ProcessNextAsync_NeedsHumanGate_AcknowledgesAndDoesNotRequeue
  Mock StepAsync → NeedsHumanGate=true. Verify Acknowledge called (gate handles re-queue).

- StartAsync_ConcurrencyLimit_OnlyNParallelInstancesProcessed
  Mock queue to return N+2 instances. Verify max N processed concurrently (SemaphoreSlim).
  N = configured Worker:Concurrency value (default 4).

**DelayedQueuePromoterJobTests (Flowamaz.Tests.Unit/Jobs/):**

- Execute_NoReadyItems_DoesNothing
  Mock ITaskQueue.GetDelayedReadyAsync → empty list. Verify EnqueueAsync never called.

- Execute_ReadyItems_PromotesToPendingQueue
  Mock returns 3 ready items. Verify EnqueueAsync called 3 times with correct workspace+instance.

- Execute_PromotionFails_LogsAndContinues
  Mock EnqueueAsync throws on second item. Verify first and third still processed.

**WorkerLeaseExpiryJobTests (Flowamaz.Tests.Unit/Jobs/):**

- Execute_NoExpiredLeases_DoesNothing
  Mock IWorkflowInstanceRepository.GetExpiredLeasesAsync → empty list.
  Verify queue.ReturnToQueueAsync never called.

- Execute_ExpiredLeases_RequeuesAll
  Mock returns 2 expired instances. Verify ReturnToQueueAsync called twice.
  Verify WorkflowEvent appended (LeaseExpired) for each.

- Execute_RequeueFails_LogsAndContinues
  First requeue succeeds, second throws. Verify third still processed.

**GateTimeoutJobTests (Flowamaz.Tests.Unit/Jobs/):**

- Execute_NoExpiredGates_DoesNothing
  Mock IGateDecisionRepository.GetExpiredPendingAsync → empty list.
  Verify no decisions updated.

- Execute_ExpiredGate_WithEscalationTarget_SetsEscalatedAndNotifies
  Mock returns gate with EscalatedTo set. Verify Decision=Escalated, notification attempted.

- Execute_ExpiredGate_NoEscalationTarget_FailsGateNode
  Mock returns gate without EscalatedTo. Verify orchestrator.FailNodeAsync called.

- Execute_OneGateFails_LogsAndProcessesRemaining
  3 expired gates, second throws on update. Verify first and third processed.

**ProcessIntelligenceJobTests (Flowamaz.Tests.Unit/Jobs/):**

- Execute_NoActiveWorkflows_DoesNothing
  Mock returns empty workflow list. Verify no AI calls, no metrics written.

- Execute_ComputesMetrics_CorrectAggregates
  Mock 10 completed instances with known durations. Verify p95, avg calculated correctly.

- Execute_SlaRiskDetected_CreatesInsight
  Mock avg_duration > sla_threshold * 0.8. Verify WorkflowInsight created with SlaRisk type.

- Execute_AiCallReturnsInsights_StoredCorrectly
  Mock IAiCompletionService returns insight list. Verify upserted to WorkflowInsight.

- Execute_AiCallFails_LogsAndContinues
  Mock AI throws. Verify deterministic SLA check still runs, error logged.

- Execute_SemanticCacheHit_SkipsAiCall
  Mock ISemanticCacheService.GetAsync returns cached response.
  Verify IAiCompletionService never called.

### Coverage measurement after all tests written:
```bash
dotnet test --collect:"XPlat Code Coverage" \
  -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.ExcludeByFile="**/Migrations/**"
```
Confirm Infrastructure/Workers ≥ 80% and Infrastructure/Jobs ≥ 80%.

### What NOT to Change
- No production code changes
- No entity or migration changes
- Tests only

## Technical Requirements
- [ ] All worker/job classes testable via constructor injection (no static dependencies)
- [ ] IServiceScopeFactory mocked using Moq to provide scoped service instances
- [ ] No real Redis, Postgres, or AI calls in unit tests — all mocked
- [ ] dotnet build — 0 errors, 0 warnings
- [ ] dotnet test — all pass (will be 220+ total)
- [ ] Infrastructure/Workers coverage ≥ 80%
- [ ] Infrastructure/Jobs coverage ≥ 80%

## Acceptance Criteria
- [ ] OrchestratorWorker: 6 unit tests all pass
- [ ] DelayedQueuePromoterJob: 3 tests all pass
- [ ] WorkerLeaseExpiryJob: 3 tests all pass
- [ ] GateTimeoutJob: 4 tests all pass
- [ ] ProcessIntelligenceJob: 6 tests all pass
- [ ] Coverlet report: Infrastructure/Workers ≥ 80%, Infrastructure/Jobs ≥ 80%

## Output Expected
```
backend/Flowamaz.Tests.Unit/Workers/OrchestratorWorkerTests.cs
backend/Flowamaz.Tests.Unit/Jobs/DelayedQueuePromoterJobTests.cs
backend/Flowamaz.Tests.Unit/Jobs/WorkerLeaseExpiryJobTests.cs
backend/Flowamaz.Tests.Unit/Jobs/GateTimeoutJobTests.cs
backend/Flowamaz.Tests.Unit/Jobs/ProcessIntelligenceJobTests.cs
```

## Notes for Executor
- BackgroundService testing: call ExecuteAsync or the internal method it delegates to.
  If the method is private, extract it to internal and use [InternalsVisibleTo].
- IServiceScopeFactory pattern: mock CreateScope() → mock IServiceScope →
  mock ServiceProvider.GetService<T>() for each dependency needed.
- ProcessIntelligenceJob AI seam: mock IAiCompletionService.CompleteAsync.
  Verify ISemanticCacheService.GetAsync called before the AI call.
