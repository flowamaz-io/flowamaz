# Flowamaz — Claude Code Execution Prompts

## ► CONTINUOUS RUN — Prompts 03 to 09 (use this now)

```
Read CLAUDE.md and FUNCTIONAL.md completely.
Read checkpoint.md — current prompt is 03-workspace-rbac-entities.

Ensure dependencies are running:
  docker compose -f infrastructure/docker-compose.dev.yml up -d

Execute all remaining Phase 1 prompts continuously without stopping:

For each prompt:
  1. Read the prompt file from prompts/phase-01/
  2. Execute fully — write all code, run dotnet build (0 errors 0 warnings),
     run dotnet test (all pass), verify acceptance criteria
  3. git add . && git commit -m "feat(scope): [prompt-id] description" && git push origin develop
  4. Log result to results.md, update checkpoint.md to next prompt
  5. /clear — then immediately read CLAUDE.md + FUNCTIONAL.md + checkpoint.md
  6. Execute next prompt — do NOT stop, do NOT wait for confirmation

Continue through prompts 03, 04, 05, 06, 07, 08, 09 without interruption.

When checkpoint.md shows PHASE_COMPLETE after prompt 09:
  Run all phase-end agents immediately:
  - Verifier deep review on entire phase output
  - UX Agent on prompts 06 and 08
  - UI Agent on prompt 06
  - Testing Agent full suite (backend + web layers)
  - Security Agent full scan including Trivy
  Compile phase-01-report.md using templates/phase-report-template.md
  Trigger .claude/hooks/notify-phase-complete.sh
  Stop and wait for human review.

Do not stop between prompts. Do not ask for confirmation. Do not open PRs.
Push directly to develop after each prompt and continue immediately.
```

---

## ► RESUME AFTER UNEXPECTED STOP

```
Read CLAUDE.md and FUNCTIONAL.md.
Read checkpoint.md to find current prompt.
Ensure dependencies running:
  docker compose -f infrastructure/docker-compose.dev.yml up -d

Resume from current prompt and continue all remaining prompts
without stopping until PHASE_COMPLETE, then run phase-end agents.
Do not stop between prompts. Do not ask for confirmation.
```

---

## ► PHASE END ONLY (if prompts done but report not generated)

```
Read CLAUDE.md, FUNCTIONAL.md, checkpoint.md, results.md.
All Phase 1 prompts complete. Run phase-end agents now:
- Verifier deep review on entire phase output
- UX Agent on prompts 06 and 08
- UI Agent on prompt 06
- Testing Agent full suite (backend + web)
- Security Agent full scan including Trivy
Compile phase-01-report.md using templates/phase-report-template.md
Trigger .claude/hooks/notify-phase-complete.sh
```

---

## After Phase 1 Complete
Upload phase-01-report.md to this ClaudeCode Foundation chat.
Chat reviews, generates fix phase + Phase 2 prompts.

---

## ► FIX PHASE 01 — Run fix-01-01 through fix-01-03 (use this now)

```
Read CLAUDE.md and FUNCTIONAL.md completely.
Read checkpoint.md — current prompt is fix-01-01-backend-coverage.
Phase: fix-phase-01.

Ensure dependencies are running:
  docker compose -f infrastructure/docker-compose.dev.yml up -d

Execute all 3 fix-phase-01 prompts continuously without stopping:

For each fix prompt:
  1. Read the prompt file from prompts/fix-phase-01/
  2. Execute fully — write all fixes and tests
  3. dotnet build (0 errors 0 warnings) + dotnet test (all pass)
  4. git add . && git commit -m "fix(scope): [fix-prompt-id] description"
  5. git push origin develop
  6. Log result to results.md, update checkpoint.md
  7. /clear — read CLAUDE.md + FUNCTIONAL.md + checkpoint.md
  8. Execute next fix prompt immediately

After fix-01-03 completes:
  Run all phase-end agents:
  - Verifier deep review on all 3 fix prompt outputs
  - Testing Agent — confirm coverage ≥ 80% and E2E 17/17 green
  - Security Agent — confirm no regressions
  Compile fix-phase-01-report.md using templates/phase-report-template.md
  Trigger .claude/hooks/notify-phase-complete.sh

Do not stop between fix prompts. Push to develop after each. Continue immediately.
```
