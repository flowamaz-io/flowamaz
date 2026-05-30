## Checkpoint
Phase: fix-phase-07
Phase Title: Phase 7 Fix — Template Format + DTO Validation + Audit + Polish
Total Prompts This Phase: 2
Completed: 0
Current Prompt: fix-07-01-critical-high
Next Prompt: fix-07-02-medium-low
Phase End Status: not started
Session Tokens: Low
Last Updated: 2026-05-30
Notes: |
  Phase 07 complete. Fix phase targets:
  CRITICAL: SfgParser reads root-level nodes but templates use spec.nodes — templates cannot publish
  HIGH: 6 new DTOs lack FluentValidation; Stripe redirect URLs unvalidated
  HIGH: instance.started audit event missing actor_user_id
  Medium/Low: breakpoint wiring, UX polish, tooltip hover, dev controller filter
  Fix report: fix-phase-07-report.md
