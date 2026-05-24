---
prompt-id: 09-phase1-integration
phase: 01
sequence: 9
roles: [Executor, Verifier, Security, Testing]
type: feature
depends-on: [08-help-content-docs]
estimated-complexity: Medium
---

# Phase 1 Integration — E2E Tests, Coverage, Final Wiring, PHASE_COMPLETE

## Context
All Phase 1 components are built. This is the final prompt. Wire everything together,
run the full test suite, verify coverage, fix any gaps, and confirm the system works
end to end. Do not add new features — only tests and wiring.

## Objective
17 Playwright E2E scenarios all pass, backend service coverage ≥ 80%, all integration
tests pass, docker compose up runs cleanly, checkpoint marks PHASE_COMPLETE.

## Scope

### What to Build

**Playwright E2E tests (web/e2e/):**

playwright.config.ts:
- baseURL: http://localhost (via Docker) or http://localhost:5173 (dev mode)
- Browser: Chromium only for Phase 1
- Screenshots on failure
- Video on failure
- Retries: 1 (flaky network tolerance)
- testDir: ./e2e

test-factories.ts (shared fixtures):
- createTestOrg() → registers a new org with unique slug, returns tokens
- createTestWorkspace(orgToken) → creates workspace, returns workspace details

---

**auth.spec.ts (6 scenarios):**

✓ S1: Register new organisation → onboarding wizard appears on first login
- POST /api/v1/auth/register with unique org slug
- Redirects to /onboarding
- Wizard step 1 visible

✓ S2: Login with valid credentials → dashboard loads with getting-started checklist
- Login with registered user
- Dashboard visible with checklist

✓ S3: Login with wrong password → actionable error shown, stays on login
- Try 3 wrong passwords
- Error message visible each time
- Still on /login

✓ S4: Account lockout after 5 failed attempts
- 5 wrong password attempts
- 6th attempt shows lockout message (not "invalid credentials")

✓ S5: Shift+? opens help panel → correct article for current screen
- Navigate to / (dashboard)
- Press Shift+?
- Help panel visible, article title matches articleMap for '/'

✓ S6: Logout clears session → /settings redirects to /login
- Login, navigate to /settings
- Click logout
- Navigate to /settings → redirected to /login

---

**workspace.spec.ts (5 scenarios):**

✓ S7: Dashboard loads → getting-started checklist visible with all 4 items
- After login
- Checklist visible
- All 4 items visible (3 grayed, 1 active — invite members)

✓ S8: AI model settings — dropdowns populated from API
- Navigate to /settings
- AI model config section visible
- F1 (Co-pilot) shows Anthropic / claude-haiku-4-5

✓ S9: Invite member → appears in members list with correct role badge
- Navigate to /settings/members
- Click Invite
- Enter email of second test user (same org)
- Submit → new member appears with Designer badge

✓ S10: Change member role → badge updates immediately
- Navigate to members list
- Change invited user from Designer to Operator
- Badge updates to Operator

✓ S11: Create API key → plain key shown once in alert → not in list
- Navigate to /settings/api-keys
- Create key with name "test-key"
- PlainKey shown in alert, starts with fmz_
- Close alert → key listed by prefix only (not plain)

---

**onboarding.spec.ts (3 scenarios):**

✓ S12: Onboarding wizard completes all 3 steps → dashboard
- Register new org
- Complete step 1 (create workspace)
- See step 2 (creation methods preview)
- Skip step 3 (invite member)
- Dashboard visible with checklist

✓ S13: Onboarding resumes from last step on page refresh
- Register new org
- Complete step 1
- Refresh page
- Step 2 visible (not step 1)

✓ S14: Getting-started checklist: dismiss → does not reappear on refresh
- Dashboard with checklist visible
- Click "I'll explore on my own"
- Checklist hidden
- Refresh → still hidden

---

**help.spec.ts (3 scenarios):**

✓ S15: Help panel search: "invite" → invite-team-members article
- Open help panel (Shift+?)
- Type "invite" in search
- First result is "Invite team members"
- Click result → article renders

✓ S16: All Phase 1 routes map to correct help article
- Navigate to each of the 7 routes in articleMap.ts
- Open help panel on each
- Verify article title matches expected article for that route

✓ S17: Error state → help article link works
- Trigger 403 (attempt action without permission)
- FmErrorState visible with help link
- Click link → help panel opens to roles-and-permissions article

---

**Backend integration test (Flowamaz.Tests.Integration/Phase1/):**

Phase1LifecycleTests.cs (Testcontainers — real PostgreSQL + Redis):

✓ Full registration lifecycle:
RegisterOrganisationAsync → LoginAsync → GenerateAccessToken → RefreshAsync →
CreateWorkspaceAsync → AddMemberAsync → GetMembersAsync → RemoveMemberAsync → LogoutAsync

✓ Workspace isolation:
Register org A + org B. Try to access org A's workspace from org B's token → 404.

✓ API key full lifecycle:
CreateApiKeyAsync → ValidateApiKeyAsync (correct scope) → ValidateApiKeyAsync
(wrong scope → fails) → RevokeApiKeyAsync → ValidateApiKeyAsync (fails)

✓ RBAC enforcement:
Register org + workspace. Add user as Viewer. Try operation requiring Admin → InsufficientRoleException.

✓ AI cost infrastructure:
- ModelResolutionService resolves workspace → org → platform correctly
- RecordUsageAsync fires and records to ai_token_usage (verify after 100ms delay)
- RateLimitService correctly counts and blocks at limit

✓ Email service:
- RegisterOrganisationAsync: welcome email attempted (check IEmailService mock called)
- EmailService with empty Resend key: logs warning, does not throw

---

**Coverage backfill:**
Run: `dotnet test --collect:"XPlat Code Coverage" -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=lcov`
Identify service methods below 80% line coverage.
Write real tests for uncovered paths — no empty tests, no skips.
Target: ≥ 80% line coverage on all Application and Infrastructure service classes.

**Final wiring checks:**
- [ ] All 15 help articles load in help panel without 404
- [ ] All 7 articleMap routes map to existing articles
- [ ] All error states have help article links that resolve
- [ ] docker compose up -d → all services healthy in < 60 seconds
- [ ] End-to-end: register → login → invite member → create API key → logout
  works via Docker (not just dev server)

**checkpoint.md — final update:**
```markdown
## Checkpoint
Phase: 01
Phase Title: Foundation
Total Prompts This Phase: 9
Completed: 9
Current Prompt: PHASE_COMPLETE
Next Prompt: awaiting Phase 2 prompts from Chat
Phase End Status: COMPLETE — awaiting phase report review
Last Updated: [today]
Notes: All 17 E2E scenarios pass. Coverage ≥ 80%. Docker deploy verified.
Upload phase report to Chat to receive Phase 2 prompts.
```

### What NOT to Build
- No new features — tests and wiring only
- No Phase 2 code — stop at phase boundary
- Do not change entity schemas or migrations

## Technical Requirements
- [ ] All 17 Playwright scenarios pass on Chromium
- [ ] All integration tests pass (Testcontainers)
- [ ] Backend service coverage ≥ 80% (measured by Coverlet)
- [ ] npm run build — zero errors
- [ ] dotnet build — zero errors, zero warnings
- [ ] docker compose -f infrastructure/docker-compose.yml up -d → all healthy

## Acceptance Criteria
- [ ] S1-S17: all pass (zero skips, zero failures)
- [ ] Coverage report: ≥ 80% on Application + Infrastructure projects
- [ ] Phase1LifecycleTests: all 6 tests pass
- [ ] Workspace isolation test: org B cannot access org A workspace
- [ ] checkpoint.md: shows PHASE_COMPLETE with correct counts
- [ ] results.md: all 9 prompts logged with completion status

## Output Expected
```
web/e2e/playwright.config.ts
web/e2e/fixtures/test-factories.ts
web/e2e/auth.spec.ts
web/e2e/workspace.spec.ts
web/e2e/onboarding.spec.ts
web/e2e/help.spec.ts
backend/Flowamaz.Tests.Integration/Phase1/Phase1LifecycleTests.cs
checkpoint.md (updated to PHASE_COMPLETE)
results.md (all 9 prompts logged)
```

## Notes for Executor
- Playwright: use page.getByRole() and page.getByLabel() — not CSS selectors.
  Semantic selectors are more resilient to styling changes.
- For S4 (lockout test): run in isolation — previous tests may have incremented
  failed login count on shared test user. Use a fresh test user.
- For S16 (all routes): write a loop over the 7 routes — not 7 separate tests.
- Integration tests: use Testcontainers.PostgreSql and Testcontainers.Redis NuGet packages.
  Each test class gets a fresh DB — use IAsyncLifetime with container start/stop.
- Coverage: run with `dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=lcov`
  then check lcov report. Document final coverage % in results.md.
- Email in integration tests: mock IEmailService — do not send real emails in tests.
