---
prompt-id: fix-05-02-ci-verification
phase: fix-phase-05
sequence: 2
roles: [Executor, Verifier, Security, Testing]
type: fix
source-issue: Testing/Security — Phase 05 report CI and coverage
severity: Medium
depends-on: [fix-05-01-integration-test-fixes]
---

# Fix: CI Verification + Coverage + Trivy

## Issues Being Fixed

1. CI pipeline needs verification — all 4 jobs must be green
2. Trivy scan must confirm 0 Critical/High on new dependencies
3. Phase 5 service coverage must be ≥ 80%
4. Branch protection status checks need enabling

## Fix 1 — Verify CI pipeline green

Push current develop to trigger CI:
```bash
git push origin develop
```

Check GitHub Actions tab at github.com/flowamaz-io/flowamaz/actions
All 4 jobs must be green:
- backend (dotnet build + test + coverage)
- web (typecheck + lint + test + build)
- cli (build + test)
- security (Trivy fs scan)

If any job fails, fix the failure before proceeding.

Common CI failures to check:
- Backend tests requiring env vars not set in CI → add to ci.yml env section
- Web tests with MSW network issues → check vitest config
- CLI build path issues → check package.json scripts

## Fix 2 — Trivy scan on new dependencies

Run Trivy locally on backend:
```bash
docker run --rm -v $(pwd)/backend:/app aquasec/trivy:latest fs /app \
  --severity CRITICAL,HIGH --exit-code 1
```

New dependency added in Phase 5: LibGit2Sharp 0.31.0
Check for any known CVEs. If found:
- If patch version available: upgrade in .csproj
- If no patch: document as accepted risk with justification

## Fix 3 — Phase 5 coverage verification

Run Coverlet on Phase 5 areas:
```bash
cd backend
dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage
dotnet tool install -g dotnet-reportgenerator-globaltool 2>/dev/null || true
reportgenerator -reports:./coverage/**/coverage.cobertura.xml \
  -targetdir:./coverage/report -reporttypes:TextSummary
cat ./coverage/report/Summary.txt
```

Target coverage ≥ 80% for:
- Flowamaz.Infrastructure/Git/WorkspaceGitService
- Flowamaz.Application/Analytics/RoiAnalyticsService
- Flowamaz.Application/Platform/EditionService
- Flowamaz.Application/Platform/ProcessTrendAnalyzer

If any area is below 80%, add targeted unit tests.

## Fix 4 — Enable branch protection status checks

Using GitHub CLI (gh):
```bash
# Verify gh is authenticated
gh auth status

# Enable required status checks on develop
gh api repos/flowamaz-io/flowamaz/rulesets \
  --method POST \
  --field name="develop-protection" \
  --field target="branch" \
  --field enforcement="active"
```

Or document the manual steps in docs/DEVELOPMENT.md:
"Go to Settings → Branches → develop → Edit → Required status checks:
Add: backend, web, cli, security"

## Fix 5 — Wire live plan/edition to UserMenu

In web/src/components/layout/UserMenu.vue:
The plan badge shows static "Starter Plan".
Wire it to the actual workspace edition:

```typescript
const ws = useWorkspaceStore()
const planLabel = computed(() => {
  const edition = ws.currentWorkspace?.edition ?? 'community'
  return {
    community: 'Community',
    starter: 'Starter Plan',
    pro: 'Pro Plan',
    enterprise: 'Enterprise'
  }[edition] ?? 'Community'
})
```

GET /api/v1/workspaces/{id} response must include edition field.
If not present, add it to WorkspaceDto from EDITION env var or org plan.

## Acceptance Criteria
- [ ] All 4 CI jobs green on GitHub Actions
- [ ] Trivy: 0 Critical, 0 High on backend dependencies
- [ ] Phase 5 service areas: coverage ≥ 80%
- [ ] UserMenu shows correct plan/edition label
- [ ] docs/DEVELOPMENT.md updated with CI status badge

## Output Expected
```
.github/workflows/ci.yml (env vars if needed)
docs/DEVELOPMENT.md (CI badge + branch protection instructions)
web/src/components/layout/UserMenu.vue (live plan label)
backend/Flowamaz.Api/ (WorkspaceDto edition field if missing)
```
