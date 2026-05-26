## Checkpoint
Phase: 02
Phase Title: Workflow Engine — Durable Execution + Observability
Total Prompts This Phase: 8
Completed: 4
Current Prompt: 02-05-workflow-interpreter
Next Prompt: 02-06-frontend-workflow-views
Phase End Status: not started
Session Tokens: Low
Last Updated: 2026-05-26
Notes: |
  Phase 01 and fix-01 complete. Phase 02 in progress.
  02-01: workflow/instance entities, event log, SKIP LOCKED claim, StartupMigrationService.
  02-02: SfgParser, WorkflowOrchestrator, RedisTaskQueue, OrchestratorWorker, DelayedQueuePromoterJob, VariableEvaluationService.
  02-03: HttpActionWorker (INodeWorker) + NodeWorkerRegistry, SagaEngine (Backward/Forward/Pivot),
  WorkerLeaseExpiryJob (15s) + GateTimeoutJob (60s).
  02-04: WorkflowDefinitions/Instances/Gates controllers + WorkflowService/InstanceService/GateService,
  InstanceStatusWebSocketHandler (/ws, polling, ?token= JWT). Build 0/0; 168 unit + 50 integration green.
  DEFERRED to 02-08: wire node executors into OrchestratorWorker loop + invoke SagaEngine from
  WorkflowOrchestrator.FailNodeAsync (full end-to-end execution loop).
  Execution model: continuous — run all 8 prompts without stopping. Push to develop after each.
  Stop only at PHASE_COMPLETE after prompt 08. Phase report: phase-02-report.md
