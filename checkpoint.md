## Checkpoint
Phase: 01
Phase Title: Foundation — Infra, Auth, Org, Workspace, Help System
Total Prompts This Phase: 9
Completed: 0
Current Prompt: GIT_INIT (run once before prompt 01)
Next Prompt: 01-backend-scaffold
Phase End Status: not started
Session Tokens: Low
Last Updated: 2026-05-24
Notes: |
  FIRST SESSION ACTIONS (before any prompt):
  1. Read CLAUDE.md — note git remote URLs
  2. Read FUNCTIONAL.md — understand full product spec
  3. Run git initialisation:
       git init
       git config user.name "Flowamaz Bot"
       git config user.email "team@flowamaz.io"
       git remote add origin https://github.com/flowamaz-io/flowamaz.git
       git checkout -b develop
       git add .
       git commit -m "chore: initial project scaffold — Flowamaz ClaudeCode Foundation"
       git push -u origin develop
  4. Update checkpoint: Current Prompt → 01-backend-scaffold
  5. Begin Phase 1 execution autonomously
