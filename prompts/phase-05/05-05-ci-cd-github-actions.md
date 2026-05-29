---
prompt-id: 05-05-ci-cd-github-actions
phase: 05
sequence: 5
roles: [Executor, Verifier, Security]
type: feature
depends-on: [05-04-roi-analytics]
estimated-complexity: Medium
---

# CI/CD Pipeline — GitHub Actions + Branch Protection Status Checks

## Context
CLI and VS Code extension exist. Now build the GitHub Actions CI pipeline that
enforces quality gates automatically on every push and PR. This enables the
status checks we left unconfigured in Phase 1 branch protection.

## Objective
Complete GitHub Actions CI pipeline for all layers (backend, web, CLI, extension).
Enable status checks on branch protection. Docker image builds and pushes on main.
Release automation with semantic versioning.

## Scope

### What to Build

**.github/workflows/ci.yml — PR and develop branch checks:**

```yaml
name: CI

on:
  push:
    branches: [develop, 'feat/**', 'fix/**']
  pull_request:
    branches: [main, develop]

jobs:
  backend:
    runs-on: ubuntu-latest
    services:
      postgres:
        image: postgres:16-alpine
        env: { POSTGRES_DB: flowamaz_test, POSTGRES_USER: flowamaz, POSTGRES_PASSWORD: test }
        options: --health-cmd pg_isready
      redis:
        image: redis:7-alpine
        options: --health-cmd "redis-cli ping"
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.x' }
      - run: dotnet restore backend/
      - run: dotnet build backend/ --no-restore -c Release
      - run: dotnet test backend/ --no-build -c Release --collect:"XPlat Code Coverage"
        env:
          DB_CONNECTION_STRING: Host=localhost;Database=flowamaz_test;Username=flowamaz;Password=test
          REDIS_CONNECTION_STRING: localhost:6379
          JWT_SECRET: ci-test-secret-minimum-32-chars-here
          CREDENTIAL_MASTER_KEY: 0000000000000000000000000000000000000000000000000000000000000000
          GATE_SIGNING_KEY: 0000000000000000000000000000000000000000000000000000000000000000
          ASPNETCORE_ENVIRONMENT: Test
      - uses: codecov/codecov-action@v4

  web:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: '22', cache: 'npm', cache-dependency-path: web/package-lock.json }
      - run: npm ci --prefix web
      - run: npm run typecheck --prefix web
      - run: npm run lint --prefix web
      - run: npm run test --prefix web
      - run: npm run build --prefix web

  cli:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: '22', cache: 'npm', cache-dependency-path: cli/package-lock.json }
      - run: npm ci --prefix cli
      - run: npm run build --prefix cli
      - run: npm test --prefix cli

  security:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: aquasecurity/trivy-action@master
        with:
          scan-type: fs
          scan-ref: ./backend
          severity: CRITICAL,HIGH
          exit-code: 1
```

**.github/workflows/release.yml — release on main merge:**

```yaml
name: Release

on:
  push:
    branches: [main]

jobs:
  build-and-push:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: docker/setup-buildx-action@v3
      - uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      - run: |
          VERSION=$(git describe --tags --abbrev=0 2>/dev/null || echo "0.0.0")
          echo "VERSION=$VERSION" >> $GITHUB_ENV
      - uses: docker/build-push-action@v5
        with:
          context: ./backend
          push: true
          tags: ghcr.io/flowamaz-io/flowamaz-backend:${{ env.VERSION }},ghcr.io/flowamaz-io/flowamaz-backend:latest
      - uses: docker/build-push-action@v5
        with:
          context: ./web
          push: true
          tags: ghcr.io/flowamaz-io/flowamaz-web:${{ env.VERSION }},ghcr.io/flowamaz-io/flowamaz-web:latest

  publish-cli:
    runs-on: ubuntu-latest
    needs: build-and-push
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: '22', registry-url: 'https://registry.npmjs.org' }
      - run: npm ci --prefix cli && npm run build --prefix cli
      - run: npm publish --prefix cli --access public
        env: { NODE_AUTH_TOKEN: ${{ secrets.NPM_TOKEN }} }
```

**.github/workflows/e2e.yml — Playwright on develop (nightly):**

```yaml
name: E2E

on:
  schedule:
    - cron: '0 2 * * *'   # 2am UTC daily
  workflow_dispatch:        # manual trigger

jobs:
  playwright:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
      - uses: actions/setup-node@v4
      - run: docker compose -f infrastructure/docker-compose.dev.yml up -d
      - run: dotnet run --project backend/Flowamaz.Api &
      - run: npm ci --prefix web && npm run dev --prefix web &
      - run: sleep 15
      - run: npx playwright install chromium
      - run: npm run e2e --prefix web
      - uses: actions/upload-artifact@v4
        if: failure()
        with:
          name: playwright-report
          path: web/playwright-report/
```

**Enable branch protection status checks:**

Now that CI exists, update branch protection on flowamaz-io/flowamaz:
- develop ruleset: add required status checks: `backend`, `web`, `cli`, `security`
- main ruleset: same + `build-and-push` must pass

Add to docs/DEVELOPMENT.md:
- How to run CI locally (act tool for GitHub Actions local run)
- Required secrets: NPM_TOKEN, GITHUB_TOKEN (auto-provided)
- How to trigger a manual release

**Required GitHub Secrets (document in DEPLOYMENT.md):**
```
NPM_TOKEN         — npm publish token for @flowamaz/cli
```

GitHub Actions automatically provides GITHUB_TOKEN for GHCR push.

**Unit tests:**
No unit tests for CI YAML files — but verify the workflow files are valid YAML
and the jobs reference correct paths.

```bash
# Verify CI YAML is valid
npx js-yaml .github/workflows/ci.yml
npx js-yaml .github/workflows/release.yml
npx js-yaml .github/workflows/e2e.yml
```

## Technical Requirements
- [ ] CI runs on every push to develop and feature branches
- [ ] Backend tests run with real Postgres + Redis (services containers)
- [ ] Web typecheck + lint + test + build all gate PR merges
- [ ] Trivy fs scan in CI — exits 1 on Critical/High
- [ ] Release workflow pushes to GHCR on main merge
- [ ] CLI published to npm on release
- [ ] E2E runs nightly on schedule

## Acceptance Criteria
- [ ] Push to develop → CI workflow triggers (verify in GitHub Actions tab)
- [ ] CI YAML files are valid (js-yaml parse passes)
- [ ] Branch protection on develop: status checks listed include backend, web, cli
- [ ] docs/DEVELOPMENT.md updated with CI instructions

## Output Expected
```
.github/workflows/ci.yml
.github/workflows/release.yml
.github/workflows/e2e.yml
docs/DEVELOPMENT.md (CI section added)
docs/DEPLOYMENT.md (secrets section added)
```
