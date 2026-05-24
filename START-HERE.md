# Flowamaz — Claude Code Start Instructions

## Paste This Into Claude Code to Begin Phase 1

```
Read CLAUDE.md and FUNCTIONAL.md completely before doing anything else.

Then perform git initialisation:
  git init (if not already initialised)
  git config user.name "Flowamaz Bot"
  git config user.email "team@flowamaz.io"
  git remote add origin https://github.com/flowamaz-io/flowamaz.git (if not already set)
  git checkout -b develop (if not already on develop)
  git add .
  git commit -m "chore: initial project scaffold — Flowamaz ClaudeCode Foundation"
  git push -u origin develop

Then update checkpoint.md — set Current Prompt to 01-backend-scaffold.

Then execute Phase 1 autonomously — run all 9 prompts sequentially,
shallow verify after each, log to results.md, update checkpoint.md,
commit and push to develop after each prompt, /clear between prompts.

When checkpoint.md shows PHASE_COMPLETE, immediately run all phase-end agents:
Verifier deep review, UX Agent on applicable prompts, UI Agent on applicable
prompts, Testing Agent full suite, Security Agent full scan including Trivy.
Compile phase-01-report.md and trigger notify-phase-complete.sh when done.

Do not stop between prompts. Do not ask for confirmation.
```

## Resume After Unexpected Stop

```
Read CLAUDE.md and checkpoint.md.
Resume autonomous execution from where checkpoint.md indicates.
Do not repeat completed prompts.
After each prompt: commit and push to develop branch on GitHub.
Continue until checkpoint.md shows PHASE_COMPLETE, then run all
phase-end agents and compile phase-01-report.md.
Do not stop between prompts. Do not ask for confirmation.
```

## After Phase 1 Complete
Upload phase-01-report.md to this ClaudeCode Foundation chat session.
Chat will generate Phase 2 prompts and fix phase prompts if needed.
