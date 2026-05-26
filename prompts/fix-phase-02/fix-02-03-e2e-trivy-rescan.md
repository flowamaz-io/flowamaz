---
prompt-id: fix-02-03-e2e-trivy-rescan
phase: fix-phase-02
sequence: 3
roles: [Executor, Verifier, Security, Testing]
type: fix
source-issue: Testing Agent + Security Agent — Phase 02 report
severity: High
depends-on: [fix-02-02-workflow-service-coverage-ai-sla]
---

# Fix: Execute Playwright S18-S25 Live + Trivy Rescan on Phase 2 Images

## Issues Being Fixed

**Issue 1 — E2E S18-S25 not executed live:**
```
ISSUE: Playwright S18–S25 written but not executed live
SEVERITY: High
FINDING: Require live stack (backend + web + db + redis, worker enabled).
FIX REQUIRED: Execute all 8 new scenarios against a live stack. Fix any failures.
```

**Issue 2 — Trivy not rerun after Phase 2 NuGet additions:**
```
ISSUE: Trivy scan was from pre-Phase-2 images
SEVERITY: Medium
FINDING: Phase 2 added Quartz.NET, YamlDotNet, Anthropic.SDK — need rescan.
FIX REQUIRED: Rebuild images with Phase 2 additions and rescan.
```

## Objective
Execute all 25 Playwright scenarios (S1-S17 from Phase 1, S18-S25 from Phase 2)
against a live Docker stack. Fix any failures. Rebuild Docker images and run Trivy
to confirm 0 Critical/High after Phase 2 dependency additions.

## Scope

### Fix 1 — Execute Playwright S18-S25 Live

**Start the full dev stack:**
```bash
# Start postgres + redis
docker compose -f infrastructure/docker-compose.dev.yml up -d

# Start backend (with worker enabled — set Worker:Enabled=true in env)
cd backend && dotnet run --project Flowamaz.Api \
  --environment Development &

# Start frontend dev server
cd web && npm run dev &

# Wait for both to be ready
sleep 5
```

**Run all E2E scenarios:**
```bash
cd web
npx playwright install chromium
npx playwright test --reporter=list
```

**The 8 new scenarios to confirm green:**

S18 — /workflows empty state with CTA visible
S19 — Create workflow via trigger modal → appears in list
S20 — Trigger instance → appears in /instances with status updating

S21 — Instance detail → Timeline tab → nodes visible with duration bars
S22 — Instance detail → Narrative tab → CEO narrative loads (stub response acceptable)
S23 — Instance detail → Narrative tab → switch Auditor → different content

S24 — Dashboard → metric cards show real numbers (not --)
S25 — Dashboard → Workflow Weather widget visible with coloured status dots

**For each failing scenario:**
- Run with --headed to see what is happening visually
- Common issues: selector mismatch, timing (add waitForResponse), API not ready
- Fix the scenario OR fix the application code (document which)
- Re-run until all pass

**For S22 (CEO narrative):**
If the real Anthropic platform key is not set in .env, the stub response is returned.
The test should accept any non-empty string as the CEO narrative content.
Update the assertion if needed: `expect(content).not.toBe('')` rather than checking
for specific text.

**Generate HTML report:**
```bash
npx playwright test --reporter=html
```
Record pass count in results.md.

**Also re-run Phase 1 scenarios S1-S17 to confirm no regressions:**
```bash
npx playwright test --reporter=list
```
All 25 scenarios (S1-S25) should pass.

### Fix 2 — Rebuild Images + Trivy Rescan

**Rebuild both images with Phase 2 code:**
```bash
docker build -t flowamaz-backend:phase2 ./backend
docker build -t flowamaz-web:phase2 ./web
```

**Run Trivy on rebuilt images:**
```bash
trivy image --severity HIGH,CRITICAL flowamaz-backend:phase2
trivy image --severity HIGH,CRITICAL flowamaz-web:phase2
```

**Also scan NuGet packages directly:**
```bash
trivy fs --severity HIGH,CRITICAL ./backend
```

**Expected result:** 0 Critical, 0 High on all three scans.

**If any HIGH/CRITICAL found:**
- Identify the vulnerable package and version
- Check if a patched version exists
- If yes: upgrade the package, rebuild, rescan
- If no patch available: document as known risk with CVSS score and exploitation context
- Any Critical finding blocks the fix phase from being marked complete

**Record all Trivy output in results.md** — package name, CVE, severity, fixed version (if any).

### What NOT to Change
- No entity changes (SlaThresholdMs migration already in fix-02-02)
- No new service logic
- Test fixes and Trivy remediation only

## Technical Requirements
- [ ] Full dev stack running during E2E: postgres, redis, backend (with worker), frontend
- [ ] All 25 Playwright scenarios pass (S1-S25)
- [ ] Playwright HTML report generated (gitignored)
- [ ] Trivy: backend image — 0 Critical, 0 High
- [ ] Trivy: web image — 0 Critical, 0 High
- [ ] Trivy: NuGet packages (fs scan) — 0 Critical, 0 High
- [ ] Any HIGH/CRITICAL finding: package upgraded or documented with justification

## Acceptance Criteria
- [ ] npx playwright test → 25 passed, 0 failed
- [ ] S18-S25: all 8 Phase 2 scenarios confirmed green
- [ ] S1-S17: all 17 Phase 1 scenarios still pass (no regressions)
- [ ] trivy image flowamaz-backend:phase2 → 0 HIGH/CRITICAL
- [ ] trivy image flowamaz-web:phase2 → 0 HIGH/CRITICAL
- [ ] trivy fs ./backend → 0 HIGH/CRITICAL (or documented exceptions)
- [ ] results.md: Trivy output recorded

## Output Expected
```
web/e2e/*.spec.ts (any selector fixes applied)
results.md (updated with 25/25 E2E pass evidence + Trivy output)
backend/*.csproj (any package version bumps for CVE fixes)
```

## Notes for Executor
- Worker must be ENABLED during E2E (unlike integration tests which disable it).
  Set environment variable: WORKER__ENABLED=true or Worker:Enabled=true in appsettings.Development.json
- S24 (dashboard real data): requires at least one workflow to exist and one instance to have run.
  The test should create a workflow and trigger an instance before checking the dashboard cards.
- S25 (Workflow Weather): requires the ProcessIntelligenceJob to have run at least once.
  Trigger it manually in the test: POST /api/v1/internal/analytics/run-now (add a dev-only endpoint)
  OR simply assert the weather widget renders (green for a healthy workflow is acceptable).
- Trivy install: if not available, install via: `curl -sfL https://raw.githubusercontent.com/aquasecurity/trivy/main/contrib/install.sh | sh -s -- -b /usr/local/bin`
