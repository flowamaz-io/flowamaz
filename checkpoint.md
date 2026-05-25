## Checkpoint
Phase: 02
Phase Title: Workflow Engine — Durable Execution + Observability
Total Prompts This Phase: 8
Completed: 2
Current Prompt: 02-03-http-worker-saga
Next Prompt: 02-04-instance-api
Phase End Status: not started
Session Tokens: Low
Last Updated: 2026-05-25
Notes: |
  Phase 01 and fix-01 complete. Phase 02 in progress.
  02-01 done: workflow/instance entities, append-only event log, repos with SKIP LOCKED claim,
  StartupMigrationService wired before app.Run().
  02-02 done: SfgParser (YamlDotNet), WorkflowOrchestrator (trigger/step/complete/fail/cancel),
  RedisTaskQueue (RPOPLPUSH claim + delayed sorted-set), OrchestratorWorker BackgroundService,
  DelayedQueuePromoterJob (Quartz every 5s), VariableEvaluationService. Node executors land in 02-03.
  Build 0/0; 160 unit + 42 integration green.
  Execution model: continuous — run all 8 prompts without stopping.
  Push to develop after each. Stop only at PHASE_COMPLETE after prompt 08.
  Phase report: phase-02-report.md
