## Checkpoint
Phase: 02
Phase Title: Workflow Engine — Durable Execution + Observability
Total Prompts This Phase: 8
Completed: 3
Current Prompt: 02-04-instance-api
Next Prompt: 02-05-workflow-interpreter
Phase End Status: not started
Session Tokens: Low
Last Updated: 2026-05-26
Notes: |
  Phase 01 and fix-01 complete. Phase 02 in progress.
  02-01: workflow/instance entities, event log, SKIP LOCKED claim, StartupMigrationService.
  02-02: SfgParser, WorkflowOrchestrator, RedisTaskQueue, OrchestratorWorker, DelayedQueuePromoterJob, VariableEvaluationService.
  02-03: HttpActionWorker (INodeWorker) + NodeWorkerRegistry, SagaEngine (Backward/Forward/Pivot),
  WorkerLeaseExpiryJob (15s) + GateTimeoutJob (60s). Build 0/0; 168 unit + 42 integration green.
  DEFERRED to 02-08: wire node executors into OrchestratorWorker loop + invoke SagaEngine from
  WorkflowOrchestrator.FailNodeAsync (full end-to-end execution loop).
  Execution model: continuous — run all 8 prompts without stopping. Push to develop after each.
  Stop only at PHASE_COMPLETE after prompt 08. Phase report: phase-02-report.md
