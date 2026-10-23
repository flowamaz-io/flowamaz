Read CLAUDE.md, FUNCTIONAL.md, checkpoint.md, results.md (fix-test-02-02 entry).

GOAL: Classify every failing E2E scenario. NO fixes in this prompt — diagnosis only.

1. Run scripts/e2e-local.sh. Load e2e-results.json.
2. For EACH failing test, open the trace / screenshot / API log and classify:
   - APP-BUG      — product behaviour contradicts FUNCTIONAL.md
   - TEST-BUG     — test asserts something FUNCTIONAL.md does not require, wrong selector, bad fixture
   - SEED/ENV     — missing seed data, env var, external dependency (Stripe, AI key, OAuth vendor)
   - NOT-BUILT    — the feature the test exercises does not really exist (UI/endpoint is a shell)
   - FLAKY        — passes on rerun (run failing tests 3x with --repeat-each=3 to confirm)
3. For each, record: scenario id (S-number), spec file, classification, one-line root cause,
   file:line of the cause, FUNCTIONAL.md section, severity (Critical/High/Medium/Low).
   Severity guide: Critical = auth, workspace isolation, data loss, workflow execution;
   High = a creation method or core page unusable; Medium = secondary feature; Low = cosmetic.
4. Group NOT-BUILT items separately — these are scope decisions for the human, not bugs.

DELIVERABLE: e2e-triage-report.md in project root (table sorted by severity) + summary counts
per classification in results.md. Update checkpoint.md → test-04-e2e-fix.
STOP after this prompt and wait for human review of the triage — the human decides what
NOT-BUILT items are in scope before any fixing.
Commit: docs(e2e): failure triage report [fix-test-02-03]
