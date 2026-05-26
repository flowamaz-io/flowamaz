---
prompt-id: fix-01-03-status-codes-docusaurus-docker
phase: fix-phase-01
sequence: 3
roles: [Executor, Verifier]
type: fix
source-issue: Verifier — Phase 01 report
severity: Medium
depends-on: [fix-01-02-e2e-and-environments]
---

# Fix: Status Code Alignment, Docusaurus Warnings, Docker Stack Smoke

## Issues Being Fixed

**Issue 1 — Status code mismatches:**
```
ISSUE: AI-config validation returns 422, prompt spec illustrated 400
FILE: WorkspaceAiConfigService / ConfigViolationException
SEVERITY: Medium
FINDING: Provider-not-allowed and capability-gate failures map to 422.
Both are 4xx. 422 is semantically more correct for validation errors but
differs from the illustrative 400 in the spec.
FIX REQUIRED: Confirm and align status code.
```
```
ISSUE: Self-modification / last-admin guards return 409, spec illustrated 400
FILE: SelfModificationException / LastAdminException
SEVERITY: Low
FINDING: 409 Conflict vs 400 Bad Request.
FIX REQUIRED: Confirm and align status code.
```

**Issue 2 — Docusaurus broken link warnings:**
```
ISSUE: ~9 broken-relative-link warnings in Docusaurus build
FILE: docs/docs/**/*.md
SEVERITY: Low
FINDING: Cross-article links fail under routeBasePath: '/' because relative
paths resolve differently. onBrokenLinks: warn keeps build green but warnings
indicate links that do not work in the published docs site.
FIX REQUIRED: Fix cross-article links or use absolute paths.
```

**Issue 3 — Full Docker stack not smoke-tested with real .env:**
```
ISSUE: docker compose up not confirmed with provisioned .env + real certs
FILE: infrastructure/docker-compose.yml
SEVERITY: Low (infra readiness)
FINDING: Partial Docker testing during phase. JWT_SECRET binding fix applied
but full stack boot with a provisioned .env not confirmed.
FIX REQUIRED: Full docker compose up smoke with real .env values.
```

## Objective
Lock in the correct HTTP status codes based on REST semantics (decision recorded
in results.md), fix all Docusaurus broken links, and confirm the full Docker
stack boots cleanly end to end.

## Scope

### Decision 1 — Status Codes (make the decision and implement it)

**AI config validation (ConfigViolationException):**
Decision: **keep 422 Unprocessable Entity**. This is the correct HTTP status for
semantically invalid input that passes structural validation. 400 is for
malformed requests. Update:
- GlobalExceptionMiddleware: ConfigViolationException → 422
- Update any tests that assert 400 for config violations → assert 422
- Update API documentation comments to state 422
- Add to the error→article map in articleMap.ts: 422 → workspaces/api-keys (or relevant)

**Self-modification / last-admin (SelfModificationException, LastAdminException):**
Decision: **keep 409 Conflict**. Attempting to remove yourself or the last admin
is a conflict with the current state of the resource, not a bad request format.
409 is semantically correct. Update:
- GlobalExceptionMiddleware: SelfModificationException, LastAdminException → 409
- Update any tests asserting 400 for these cases → assert 409
- Document these in API comments

**Update FUNCTIONAL.md §4 to record the decisions:**
Add a note under the RBAC section:
```
HTTP Status Codes (confirmed fix-01):
- ConfigViolationException → 422 Unprocessable Entity
- SelfModificationException → 409 Conflict
- LastAdminException → 409 Conflict
- InsufficientRoleException → 403 Forbidden
- WorkspaceNotFoundException → 404 Not Found
```

### Fix 2 — Docusaurus broken links

In docs/docs/**/*.md, all cross-article links must use paths that work under
`routeBasePath: '/'`.

Common patterns to fix:
- `[invite members](../workspaces/invite-team-members)` — works if relative path is correct
- `[plans comparison](../billing/plans-comparison)` — check depth

Approach:
1. Run `npm run build` in the docs/ directory
2. Read all warnings — each one shows the broken link and the file it is in
3. Fix each link — use paths relative to the docs root e.g. `/workspaces/invite-team-members`
   rather than relative file paths for cross-section links
4. Re-run build until 0 warnings
5. Change `onBrokenLinks: 'warn'` to `onBrokenLinks: 'throw'` in docusaurus.config.js
   so future broken links fail the build immediately

### Fix 3 — Docker stack smoke test

Ensure a working .env file exists for local use (copy .env.example, fill minimum
required values for local smoke):
```
DB_PASSWORD=flowamaz_dev
JWT_SECRET=dev-only-secret-minimum-32-chars-long-here
RESEND_API_KEY=re_placeholder (email logs warning, does not throw)
```

Generate dev SSL cert if not already present:
```bash
cd infrastructure/nginx/ssl && bash generate-dev-cert.sh
```

Start full stack:
```bash
docker compose -f infrastructure/docker-compose.yml up -d
```

Verify:
- [ ] All 5 containers healthy (proxy, backend, web, db, redis)
- [ ] GET https://localhost/health → {"status":"healthy","dependencies":{"db":"healthy","redis":"healthy"}}
- [ ] GET https://localhost/scalar → 200 Scalar UI renders
- [ ] GET https://localhost → Vue app loads (may need to accept self-signed cert)
- [ ] POST https://localhost/api/v1/auth/register → creates org and returns tokens
- [ ] HTTP → HTTPS redirect: GET http://localhost → 301

Record results in results.md.

### What NOT to Change
- No entity changes
- No migration changes
- No test logic changes (only updating status code assertions)
- No frontend feature changes

## Technical Requirements
- [ ] ConfigViolationException → 422 in GlobalExceptionMiddleware
- [ ] SelfModificationException, LastAdminException → 409 in GlobalExceptionMiddleware
- [ ] All existing tests updated to assert the confirmed status codes
- [ ] FUNCTIONAL.md §4 updated with HTTP status code decisions
- [ ] Docusaurus build: 0 broken link warnings, onBrokenLinks: 'throw'
- [ ] Full Docker stack: all 5 containers healthy, all 6 smoke checks pass
- [ ] dotnet build — 0 errors, 0 warnings
- [ ] dotnet test — all tests pass with updated status code assertions

## Acceptance Criteria
- [ ] POST /workspaces/{id}/ai-config with invalid provider → 422 (not 400)
- [ ] PATCH /workspaces/{id}/members/{userId}/role with own userId → 409 (not 400)
- [ ] npm run build in docs/ → 0 broken link warnings
- [ ] docusaurus.config.js: onBrokenLinks: 'throw'
- [ ] docker compose up → GET /health → {"status":"healthy","dependencies":{"db":"healthy","redis":"healthy"}}

## Output Expected
```
backend/Flowamaz.Api/Middleware/GlobalExceptionMiddleware.cs (status codes updated)
backend/Flowamaz.Tests.Integration/ (status code assertions updated)
FUNCTIONAL.md (HTTP status code section added to §4)
docs/docusaurus.config.js (onBrokenLinks: 'throw')
docs/docs/**/*.md (broken links fixed)
results.md (Docker smoke test evidence recorded)
```

## Notes for Executor
- When updating status code tests, search for `.Should().Be(HttpStatusCode.BadRequest)`
  and `.Should().Be(StatusCodes.Status400BadRequest)` across all test files —
  only change the ones for ConfigViolation, SelfModification, LastAdmin.
- Docusaurus: run `npm run build -- --no-minify 2>&1 | grep "broken"` to list
  all broken links quickly.
- Docker smoke: use `docker compose ps` to confirm all containers are healthy.
  Use `docker compose logs backend --tail=20` if /health returns unhealthy.
- The .env used for Docker smoke must NOT be committed. Confirm .gitignore covers it.
