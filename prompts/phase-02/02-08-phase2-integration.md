---
prompt-id: 02-08-phase2-integration
phase: 02
sequence: 8
roles: [Executor, Verifier, Security, Testing]
type: feature
depends-on: [02-07-process-intelligence]
estimated-complexity: Medium
---

# Phase 2 Integration — E2E Tests, Coverage, PHASE_COMPLETE

## Context
All Phase 2 components built. Final prompt: integration tests, E2E scenarios,
coverage verification, and PHASE_COMPLETE.

## Objective
Full end-to-end workflow lifecycle tests, coverage ≥ 80% on all Phase 2 service
classes, Playwright E2E scenarios for new views, and Phase 2 report.

## Scope

### What to Build

**Backend integration tests (Flowamaz.Tests.Integration/Phase2/):**

Phase2LifecycleTests.cs — full workflow lifecycle:
1. Create workflow definition with valid YAML
2. Parse and validate YAML → WorkflowGraph
3. Publish (create WorkflowVersion)
4. Trigger instance with idempotency key
5. Trigger same instance again with same key → returns same instance
6. Poll instance status → Completed (use simple test YAML with no external calls)
7. Get timeline → nodes in correct order with durations
8. Get narrative (CEO audience) → contains workflow name
9. Get narrative (Auditor audience) → contains all node timestamps
10. Cancel a Running instance → status=Cancelled, event appended

SagaTests.cs:
- Backward compensation: A→B→C fails at C → compensates C-comp, B-comp, A-comp in order
- Forward strategy: fail at B → retry B → succeeds → continues to C

WorkerConcurrencyTests.cs:
- 10 instances triggered simultaneously → all claimed by workers (no double-claim)
- Worker lease expiry: kill a worker mid-processing → timer job recovers the instance

**Playwright E2E (add to Phase 1 suite):**

workflow.spec.ts (new scenarios):
- S18: Navigate to /workflows → empty state visible with CTA
- S19: Create workflow (trigger modal from dashboard) → appears in list
- S20: Trigger instance → appears in /instances with Pending→Running status

instance.spec.ts (new scenarios):
- S21: Instance detail → timeline tab → nodes visible with duration bars
- S22: Instance detail → narrative tab → CEO narrative loads
- S23: Instance detail → narrative tab → switch to Auditor → different content

dashboard.spec.ts (new scenarios):
- S24: Dashboard → metric cards show real numbers (not --)
- S25: Dashboard → Workflow Weather widget visible (green status for healthy workflow)

**Coverage check:**
Run Coverlet across all Phase 2 service classes:
- Flowamaz.Application/Workflow/* — target ≥ 80%
- Flowamaz.Application/Analytics/* — target ≥ 80%
- Flowamaz.Infrastructure/Workers/* — target ≥ 80%
- Flowamaz.Infrastructure/Jobs/* — target ≥ 80%

**Security scan:**
Run Trivy on updated Docker images.
Static scan on Phase 2 diff:
- No AI API keys in code or config files
- WorkflowVariable sensitive values: encrypted at rest, masked in API
- WebSocket: JWT validation before connection accepted
- Debugger: Production guard enforced at service layer

**checkpoint.md — final update:**
```
Phase: 02
Next Prompt: PHASE_COMPLETE
Completed: 8
Phase End Status: PHASE_COMPLETE
```

## Technical Requirements
- [ ] Phase2LifecycleTests: 10-step workflow lifecycle all pass
- [ ] SagaTests: backward compensation order correct
- [ ] WorkerConcurrencyTests: 10 concurrent instances, no double-claim
- [ ] All 8 new Playwright scenarios pass (S18-S25)
- [ ] Phase 2 service coverage ≥ 80% (Coverlet report)
- [ ] Trivy: 0 Critical, 0 High on updated images
- [ ] checkpoint.md: PHASE_COMPLETE

## Acceptance Criteria
- [ ] Full lifecycle test: trigger → complete → timeline → narrative all pass
- [ ] Backward saga: compensations in reverse order (C-comp before B-comp before A-comp)
- [ ] Concurrent worker test: 10 instances, 0 double-claims
- [ ] S18-S25: all 8 new Playwright scenarios pass
- [ ] Coverage ≥ 80% on Phase 2 service classes

## Output Expected
```
backend/Flowamaz.Tests.Integration/Phase2/Phase2LifecycleTests.cs
backend/Flowamaz.Tests.Integration/Phase2/SagaTests.cs
backend/Flowamaz.Tests.Integration/Phase2/WorkerConcurrencyTests.cs
web/e2e/workflow.spec.ts (S18-S20)
web/e2e/instance.spec.ts (S21-S23)
web/e2e/dashboard.spec.ts (S24-S25)
checkpoint.md (PHASE_COMPLETE)
results.md (all 8 prompts logged)
```
