---
prompt-id: 05-08-phase5-integration
phase: 05
sequence: 8
roles: [Executor, Verifier, Security, Testing]
type: feature
depends-on: [05-07-community-edition]
estimated-complexity: Medium
---

# Phase 5 Integration — E2E Tests, Coverage, CI Validation, PHASE_COMPLETE

## Context
All Phase 5 components built. Final prompt: integration tests, E2E for new views,
CI pipeline validation, coverage, security scan, PHASE_COMPLETE.

## Objective
Phase 5 lifecycle tests, Playwright E2E for Git versioning, analytics, and sidebar,
CI pipeline confirmed green, coverage ≥ 80%, PHASE_COMPLETE.

## Scope

### What to Build

**Backend integration tests (Phase5/):**

Phase5GitTests.cs:
- Commit workflow → history shows commit with correct SHA
- Get workflow at commit SHA → correct YAML returned
- Diff two commits → added/removed nodes correctly identified
- Publish → tag created with correct name

Phase5EditionTests.cs:
- Community: create 5 workflows → OK, 6th → 429 plan_limit_exceeded
- Community: trigger 500 runs → OK, 501st → 429
- Upgrade URL in 429 response body

Phase5RoiTests.cs:
- Configure ROI → get ROI → correct time saved + cost avoided calculated

**Playwright E2E (add to suite):**

git.spec.ts:
- S40: Workflow detail → Versions tab → commit history visible
- S41: Diff between two versions → colour-coded lines visible

analytics.spec.ts:
- S42: /analytics → ROI view renders with date range picker
- S43: Configure ROI for workflow → numbers appear in table

sidebar.spec.ts:
- S44: Workspace switcher opens dropdown, shows workspaces
- S45: Sidebar collapse → icon-only mode, tooltips on hover
- S46: Community edition → edition banner visible in sidebar

cli.spec.ts:
- S47: fmz workflow validate valid-workflow.yaml → exits 0
- S48: fmz workflow validate invalid-workflow.yaml → exits 1, shows error

Total E2E: 48 scenarios (S1-S39 from phases 1-4 + S40-S48 Phase 5)
Also re-run S34-S39 (Phase 4 E2E that was not run live in fix-04).

**CI pipeline validation:**

Push to develop → verify GitHub Actions workflow triggers.
Check all 4 jobs (backend, web, cli, security) appear in Actions tab.
If any job fails: fix before marking PHASE_COMPLETE.

**Coverage check (Phase 5 areas):**
- Application/Git/* ≥ 80%
- Application/Analytics (ROI) ≥ 80%
- Application/Platform/EditionService ≥ 80%
- CLI src/ (Vitest) ≥ 80%

**Security scan:**
Trivy on rebuilt images (Phase 5 adds LibGit2Sharp).
Verify install.sh does not hardcode secrets (grep clean).

**Final sidebar visual check:**
Run application, navigate all routes, confirm:
- Dark sidebar renders correctly
- All nav items navigate without errors
- Gates badge updates with real count
- Workspace switcher works

**checkpoint.md final:**
```
Phase: 05
Next Prompt: PHASE_COMPLETE
Completed: 8
Phase End Status: PHASE_COMPLETE
```

## Technical Requirements
- [ ] Phase5GitTests: commit/history/diff/publish all pass
- [ ] Phase5EditionTests: community limits enforced at service layer
- [ ] S40-S48 all pass + S34-S39 confirmed green
- [ ] CI pipeline: all 4 jobs green on develop push
- [ ] Phase 5 service coverage ≥ 80% all areas
- [ ] Trivy: 0 Critical/High
- [ ] install.sh: grep for hardcoded secrets → 0 results

## Acceptance Criteria
- [ ] Git diff test: add a node → diff shows it in added[]
- [ ] Edition limit: 6th workflow → 429 with upgrade_url in body
- [ ] S40-S48: all 9 new scenarios pass
- [ ] S34-S39: all 6 Phase 4 scenarios confirmed green
- [ ] GitHub Actions: all 4 CI jobs green
- [ ] Trivy: 0 Critical/High

## Output Expected
```
backend/Flowamaz.Tests.Integration/Phase5/Phase5GitTests.cs
backend/Flowamaz.Tests.Integration/Phase5/Phase5EditionTests.cs
backend/Flowamaz.Tests.Integration/Phase5/Phase5RoiTests.cs
web/e2e/git.spec.ts (S40-S41)
web/e2e/analytics.spec.ts (S42-S43)
web/e2e/sidebar.spec.ts (S44-S46)
web/e2e/cli.spec.ts (S47-S48)
checkpoint.md (PHASE_COMPLETE)
results.md (all 8 prompts logged)
```
