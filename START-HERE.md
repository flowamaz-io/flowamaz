# Flowamaz — Claude Code Start Instructions

## Resume Prompt (use this for every session from prompt 02 onwards)

```
Read CLAUDE.md and FUNCTIONAL.md.
Read checkpoint.md to confirm current prompt.
Bring up dependencies if not running:
  docker compose -f infrastructure/docker-compose.dev.yml up -d

Execute the current prompt from checkpoint.md fully.
When complete, verify acceptance criteria from the prompt file.
Then:
  git checkout -b feat/phase-{NN}-prompt-{NN}-{prompt-slug}
  git add .
  git commit -m "feat(scope): [prompt-id] description"
  git push origin feat/phase-{NN}-prompt-{NN}-{prompt-slug}
  gh pr create --base develop --title "feat(scope): [prompt-id]" --body "See results.md"

Log result to results.md and update checkpoint.md.
Do not proceed to the next prompt — stop here. Human merges the PR.
```

## Phase End Prompt (when checkpoint shows PHASE_COMPLETE)

```
Read CLAUDE.md, FUNCTIONAL.md, checkpoint.md, and results.md.
All Phase 1 prompts are complete. Run all phase-end agents:
- Verifier deep review on entire phase output
- UX Agent on prompts 06 and 08
- UI Agent on prompt 06
- Testing Agent full suite (backend + web)
- Security Agent full scan including Trivy
Compile phase-01-report.md using templates/phase-report-template.md.
Trigger notify-phase-complete.sh when done.
```

## After Phase Complete
Upload phase-01-report.md to the ClaudeCode Foundation chat session.
Chat reviews, generates fix prompts and Phase 2 prompts.

## Git Workflow Note
develop branch has protection — direct push is rejected.
Every prompt lands as a separate PR to develop.
You merge the PR, then start the next session for the next prompt.
