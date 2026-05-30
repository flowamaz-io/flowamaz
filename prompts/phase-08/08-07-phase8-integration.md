---
prompt-id: 08-07-phase8-integration
phase: 08
sequence: 7
roles: [Executor, Verifier, Security, Testing]
type: feature
depends-on: [08-06-final-ui-polish]
estimated-complexity: Medium
---

# Phase 8 Integration — Final E2E, Security, Coverage, PHASE_COMPLETE

## Context
All Phase 8 components complete. Final prompt: integration tests, E2E S73-S80,
security scan, coverage, PHASE_COMPLETE — the last phase before comprehensive
testing session.

## Objective
Phase 8 final integration, E2E completion to S80, security scan, PHASE_COMPLETE.

## Scope

### What to Build

**Backend integration tests (Phase8/):**

Phase8WorkspaceTests.cs:
- Archive workspace → status=Archived, triggers disabled
- Leave workspace as last admin → 409 Conflict
- Create workspace from switcher → switches to new workspace

Phase8DeferredFixTests.cs:
- Install official template → publish → trigger → instance created (the Critical fix)
- CheckoutRequest with external URL → validator rejects (400)
- Breakpoint set → StepAsync pauses instance at that node

**Playwright E2E (final batch S73-S80):**

workspace.spec.ts:
- S73: Workspace switcher search filters workspaces
- S74: Create workspace from switcher → new workspace active

marketing.spec.ts:
- S75: flowamaz.com home page renders without auth
- S76: /pricing page shows plan cards

documentation.spec.ts:
- S77: Help panel search returns results for "connector"
- S78: /api-docs renders Scalar with public endpoints

launch.spec.ts:
- S79: GET /health returns all 5 dependencies healthy
- S80: Install official template → publish succeeds (Critical fix verified E2E)

Total E2E: 80 scenarios (S1-S72 existing + S73-S80 Phase 8)

**Security final scan:**

Trivy fs scan on entire project:
```bash
docker run --rm -v $(pwd)/backend:/app aquasec/trivy:latest \
  fs /app --severity CRITICAL,HIGH --ignore-unfixed
```

Expected: 0 Critical, 0 High across all dependencies (Phases 1-8).

Security review focus areas for Phase 8:
- DTO validators: CheckoutRequest URL allowlist working
- Template install→publish: no YAML injection path
- Marketing site: no API keys or secrets in static bundle
- Rate limit: co-pilot 20/hour enforced

**Coverage final check:**

Run coverage on Phase 8 additions:
- WorkspaceService archive/leave ≥ 80%
- StripeService validators ≥ 80%
- Phase 8 deferred fixes ≥ 80%

**Final production readiness verification:**

Run the production readiness checklist:
1. dotnet build Flowamaz.sln → 0/0 ✓
2. dotnet test → all pass ✓
3. npm run build --prefix web → 0/0 ✓
4. npm run build --prefix marketing → 0/0 ✓
5. Trivy → 0 Critical/High ✓
6. grep -r "localhost\|hardcoded\|TODO" backend/Flowamaz.Api/ → 0 results ✓
7. grep -r "console\.log\|Debug\.WriteLine" backend/ → 0 results ✓
8. All env vars documented in .env.example ✓

**Final checkpoint:**
```
Phase: 08
Next Prompt: PHASE_COMPLETE
Completed: 7
Phase End Status: PHASE_COMPLETE
```

After PHASE_COMPLETE — the application is ready for the comprehensive
testing session covering all phases 1-8 end to end.

## Acceptance Criteria
- [ ] install official template → publish → trigger integration test passes
- [ ] S73-S80: all 8 E2E scenarios authored
- [ ] Trivy: 0 Critical/High
- [ ] Production readiness checklist: all 8 items ✓
- [ ] Total: unit ≥ 545, integration ≥ 115, web unit ≥ 60

## Output Expected
```
backend/Flowamaz.Tests.Integration/Phase8/Phase8WorkspaceTests.cs
backend/Flowamaz.Tests.Integration/Phase8/Phase8DeferredFixTests.cs
web/e2e/workspace.spec.ts (S73-S74)
web/e2e/marketing.spec.ts (S75-S76)
web/e2e/documentation.spec.ts (S77-S78)
web/e2e/launch.spec.ts (S79-S80)
checkpoint.md (PHASE_COMPLETE)
results.md (all 7 prompts logged)
```
