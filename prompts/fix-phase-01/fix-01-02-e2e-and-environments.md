---
prompt-id: fix-01-02-e2e-and-environments
phase: fix-phase-01
sequence: 2
roles: [Executor, Verifier, UX, Testing]
type: fix
source-issue: Testing Agent + Verifier — Phase 01 report
severity: High
depends-on: [fix-01-01-backend-coverage]
---

# Fix: E2E Suite Not Executed Live + Environments Endpoint Missing

## Issues Being Fixed

**Issue 1 — E2E not executed live:**
```
ISSUE: Playwright E2E suite written but not executed live
SCREEN/FLOW: All Phase 1 screens
SEVERITY: High
FINDING: 17 scenarios written and committed but were not run against a live
stack during the autonomous session.
FIX REQUIRED: Execute Playwright S1-S17 against the dev stack, fix any
selector/behaviour gaps, confirm all green.
```

**Issue 2 — Environments endpoint missing:**
```
ISSUE: ApiKeysView environment selection is a workaround
FILE: web/src/views/workspace/ApiKeysView.vue
SEVERITY: Medium
FINDING: No GET /workspaces/{id}/environments endpoint exists. The UI uses a
workaround (environment IDs seen on existing keys + manual UUID field).
FIX REQUIRED: Add GET /workspaces/{id}/environments and a proper selector.
```

## Objective
Add the missing environments endpoint, update the frontend to use it,
then execute all 17 Playwright scenarios against a live dev stack and
confirm every scenario passes. Fix any selector or behaviour gaps found.

## Scope

### What to Build and Fix

**Backend — Add GET /workspaces/{id}/environments:**

In WorkspacesController (or a new WorkspaceEnvironmentsController):
```
GET /api/v1/workspaces/{id}/environments
[RequireWorkspaceRole(Viewer)]
Returns: [{ id, name, workspaceId, createdAt }]
```
- Returns all 3 environments (Dev, Staging, Production) for the workspace
- Ordered: Dev, Staging, Production
- Add to IWorkspaceService and WorkspaceService:
  GetEnvironmentsAsync(workspaceId) → List<WorkspaceEnvironment>
- Integration test: returns 3 environments after workspace creation

**Frontend — Fix ApiKeysView environment selector:**
- GET /api/v1/workspaces/{id}/environments on component mount
- Replace manual UUID field with a proper select dropdown
- Options: Dev, Staging, Production (loaded from API)
- Show environment name in the keys list table (not just the raw ID)
- Loading and error states for the environments fetch

**Execute Playwright E2E suite:**

Start the full dev stack:
```bash
docker compose -f infrastructure/docker-compose.dev.yml up -d
cd backend && dotnet run --project Flowamaz.Api &
cd web && npm run dev &
npx playwright install chromium
```

Run all 17 scenarios:
```bash
cd web && npx playwright test --reporter=list
```

For each failing scenario:
- Identify the root cause (selector mismatch, timing issue, API response change)
- Fix the test OR fix the application behaviour (document which)
- Re-run until green

**The 17 scenarios to confirm green (from prompt 09):**

auth.spec.ts:
- S1: Register → onboarding wizard appears
- S2: Login → dashboard with getting-started checklist
- S3: Wrong password → actionable error, stays on login
- S4: 5 failed logins → lockout message on 6th attempt
- S5: Shift+? → help panel opens → correct article for current screen
- S6: Logout → /settings redirects to /login

workspace.spec.ts:
- S7: Dashboard → getting-started checklist with all 4 items
- S8: AI model settings → dropdowns populated from API
- S9: Invite member → appears in list with correct role badge
- S10: Change member role → badge updates immediately
- S11: Create API key → plain key shown once → not in list

onboarding.spec.ts:
- S12: Complete all 3 wizard steps → dashboard
- S13: Wizard resumes from last step on refresh
- S14: Dismiss checklist → does not reappear on refresh

help.spec.ts:
- S15: Search "invite" → invite-team-members article found
- S16: All 7 Phase 1 routes open correct default article
- S17: Force 403 → error state shows help article link

**Screenshot evidence:**
After all 17 pass, save Playwright HTML report:
```bash
npx playwright test --reporter=html
```
This generates playwright-report/index.html — include the pass count in results.md.

### What NOT to Change
- No entity schema changes beyond adding the environments endpoint
- No changes to existing passing tests
- No changes to auth or RBAC logic

## Technical Requirements
- [ ] GET /api/v1/workspaces/{id}/environments returns 200 with 3 environments
- [ ] Endpoint requires minimum Viewer role
- [ ] Returns environments in Dev/Staging/Production order
- [ ] ApiKeysView: environments loaded from API, shown as dropdown
- [ ] ApiKeysView: environment name shown in keys list (not raw UUID)
- [ ] All 17 Playwright scenarios pass on Chromium
- [ ] Playwright HTML report generated showing 17/17 pass
- [ ] No TypeScript errors after frontend changes
- [ ] dotnet build — 0 errors, 0 warnings after backend addition
- [ ] Integration test: workspace creation → GET environments returns 3 items

## Acceptance Criteria
- [ ] GET /api/v1/workspaces/{id}/environments → 200 with 3 environment objects
- [ ] Viewer-role user can call the endpoint (not just Admin)
- [ ] S1-S17: all 17 pass (npx playwright test shows 17 passed, 0 failed)
- [ ] ApiKeysView environment selector: shows Dev / Staging / Production by name
- [ ] results.md updated with Playwright pass evidence

## Output Expected
```
backend/Flowamaz.Api/Controllers/WorkspacesController.cs (environments endpoint added)
backend/Flowamaz.Application/Workspace/DTOs/WorkspaceEnvironmentDto.cs
backend/Flowamaz.Application/Workspace/Services/WorkspaceService.cs (GetEnvironmentsAsync added)
backend/Flowamaz.Tests.Integration/Workspace/EnvironmentsApiTests.cs
web/src/views/workspace/ApiKeysView.vue (environments dropdown fixed)
web/src/services/workspace.service.ts (getEnvironments method added)
web/playwright-report/ (HTML report — do not commit this directory)
results.md (updated with E2E pass count and evidence)
```

## Notes for Executor
- Run playwright with --headed flag first to watch the tests visually if any fail:
  `npx playwright test --headed --slowMo=500`
- Common failure causes in E2E: timing (add waitForResponse or waitForSelector),
  selector specificity (use getByRole/getByLabel not CSS), API not ready on load.
- The S4 lockout test: use a dedicated test user, not the shared fixture user,
  to avoid contaminating other tests with a locked account.
- If S16 (all routes → correct help article) fails on any route, check articleMap.ts
  matches the actual route paths in the router.
- Screenshot on failure is already configured in playwright.config.ts.
