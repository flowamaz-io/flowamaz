## Checkpoint
Phase: 02
Phase Title: Workflow Engine — Durable Execution + Observability
Total Prompts This Phase: 8
Completed: 1
Current Prompt: 02-02-orchestrator-task-queue
Next Prompt: 02-03-http-worker-saga
Phase End Status: not started
Session Tokens: Low
Last Updated: 2026-05-25
Notes: |
  Phase 01 and fix-01 complete. Phase 02 in progress.
  02-01 done: workflow/instance entities, append-only event log, repos with SKIP LOCKED
  worker claim, StartupMigrationService wired before app.Run() (Docker self-bootstraps schema).
  Build 0/0; 149 unit + 38 integration green. Migration: 20260525124915_AddWorkflowSchema.
  Execution model: continuous — run all 8 prompts without stopping.
  Push to develop after each. Stop only at PHASE_COMPLETE after prompt 08.
  Phase report: phase-02-report.md
