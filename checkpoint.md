## Checkpoint
Phase: 02
Phase Title: Workflow Engine — Durable Execution + Observability
Total Prompts This Phase: 8
Completed: 5
Current Prompt: 02-06-frontend-workflow-views
Next Prompt: 02-07-process-intelligence
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
  InstanceStatusWebSocketHandler (/ws, polling, ?token= JWT).
  02-05: WorkflowInterpreterService (CEO/Auditor/Developer), AiCompletionService seam, enriched timeline,
  StepDebuggerService (Dev/Staging-only, Redis state). Build 0/0; 174 unit + 50 integration green.
  Background worker + Quartz gated by Worker:Enabled (off in integration tests via Worker__Enabled=false).
  DEFERRED to 02-08: wire node executors into OrchestratorWorker loop, invoke SagaEngine from
  WorkflowOrchestrator.FailNodeAsync, and have the worker honour debugger pause points (full E2E loop).
  Execution model: continuous — run all 8 prompts without stopping. Push to develop after each.
  Stop only at PHASE_COMPLETE after prompt 08. Phase report: phase-02-report.md
