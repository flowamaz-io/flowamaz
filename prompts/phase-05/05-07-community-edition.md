---
prompt-id: 05-07-community-edition
phase: 05
sequence: 7
roles: [Executor, Verifier, Security]
type: feature
depends-on: [05-06-sidebar-design-update]
estimated-complexity: Medium
---

# Community Edition Packaging + get.flowamaz.io Installer

## Context
The platform is feature-complete for community use. Now package the community
edition as a single-command installer and prepare it for public release.

## Objective
One-command Docker install, community edition limits enforced (5 workflows, 500 runs,
1 user), edition detection throughout the app, and installer script at get.flowamaz.io.

## Scope

### What to Build

**Edition enforcement service:**

`IEditionService` + `EditionService`:
Reads `EDITION` env var (community/starter/pro/enterprise).

`GetEditionLimitsAsync(ct)` → EditionLimits
Returns limits based on current edition (from Plan seed data).

`CheckLimitAsync(workspaceId, limitType, ct)` → LimitCheckResult
- limitType: WorkflowCount, RunsThisMonth, MemberCount
- If community edition AND limit would be exceeded: return LimitReached=true
- If cloud edition: defer to Plan limits from DB

Enforce in relevant service methods:
- WorkflowDefinitionService.CreateAsync: check WorkflowCount limit
- WorkflowOrchestrator.TriggerAsync: check RunsThisMonth limit
- WorkspaceMemberService.AddMemberAsync: check MemberCount limit

On limit reached: throw PlanLimitException with message and upgrade URL.
PlanLimitException → 429 Too Many Requests with:
```json
{
  "error": "plan_limit_exceeded",
  "message": "Community edition limit: 5 workflows. Upgrade to Starter for unlimited workflows.",
  "upgrade_url": "https://flowamaz.com/pricing",
  "limit_type": "workflow_count",
  "current_value": 5,
  "limit_value": 5
}
```

**Community edition Docker packaging:**

`infrastructure/community/docker-compose.yml`:
Single-file Docker Compose that runs the entire Flowamaz stack:
```yaml
# flowamaz-community.yml
# Run with: docker compose -f flowamaz-community.yml up -d

version: '3.9'
services:
  flowamaz:
    image: ghcr.io/flowamaz-io/flowamaz-backend:latest
    environment:
      EDITION: community
      DB_CONNECTION_STRING: Host=db;Database=flowamaz;Username=flowamaz;Password=${POSTGRES_PASSWORD:-flowamaz_local}
      REDIS_CONNECTION_STRING: redis:6379
      JWT_SECRET: ${JWT_SECRET:-CHANGE_THIS_IN_PRODUCTION}
      CREDENTIAL_MASTER_KEY: ${CREDENTIAL_MASTER_KEY:-CHANGE_THIS_IN_PRODUCTION}
      GATE_SIGNING_KEY: ${GATE_SIGNING_KEY:-CHANGE_THIS_IN_PRODUCTION}
    depends_on: [db, redis]
    restart: unless-stopped

  web:
    image: ghcr.io/flowamaz-io/flowamaz-web:latest
    restart: unless-stopped

  proxy:
    image: nginx:alpine
    ports: ["3000:80"]
    volumes: [./nginx-community.conf:/etc/nginx/nginx.conf:ro]
    depends_on: [flowamaz, web]
    restart: unless-stopped

  db:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: flowamaz
      POSTGRES_USER: flowamaz
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-flowamaz_local}
    volumes: [pgdata:/var/lib/postgresql/data]
    restart: unless-stopped

  redis:
    image: redis:7-alpine
    volumes: [redisdata:/data]
    restart: unless-stopped

volumes: { pgdata: {}, redisdata: {} }
```

Community edition: HTTP only (port 3000), no SSL (simpler for local installs).

**Install script (get.flowamaz.io/community):**

`infrastructure/community/install.sh`:
```bash
#!/bin/bash
set -e

echo "Installing Flowamaz Community Edition..."

# Check prerequisites
command -v docker >/dev/null || { echo "Docker required. Install from docker.com"; exit 1; }
command -v docker compose >/dev/null || { echo "Docker Compose required."; exit 1; }

# Download compose file
mkdir -p ~/.flowamaz
curl -fsSL https://get.flowamaz.io/community/docker-compose.yml \
  -o ~/.flowamaz/docker-compose.yml

# Generate secrets if not already set
if [ ! -f ~/.flowamaz/.env ]; then
  echo "Generating secure configuration..."
  JWT_SECRET=$(openssl rand -hex 32)
  CREDENTIAL_MASTER_KEY=$(openssl rand -hex 32)
  GATE_SIGNING_KEY=$(openssl rand -hex 32)
  POSTGRES_PASSWORD=$(openssl rand -hex 16)

  cat > ~/.flowamaz/.env << EOF
JWT_SECRET=$JWT_SECRET
CREDENTIAL_MASTER_KEY=$CREDENTIAL_MASTER_KEY
GATE_SIGNING_KEY=$GATE_SIGNING_KEY
POSTGRES_PASSWORD=$POSTGRES_PASSWORD
EOF
  echo "Configuration saved to ~/.flowamaz/.env"
fi

# Start
docker compose -f ~/.flowamaz/docker-compose.yml --env-file ~/.flowamaz/.env up -d

echo ""
echo "✓ Flowamaz Community Edition is running!"
echo "  Open: http://localhost:3000"
echo "  To stop: docker compose -f ~/.flowamaz/docker-compose.yml down"
```

**Frontend — Edition badges and upgrade prompts:**

Edition banner (shown in community edition, top of sidebar):
```
┌─────────────────────────────┐
│ Community Edition           │
│ 3/5 workflows used  Upgrade │
└─────────────────────────────┘
```

PlanLimitException → FmAlert in the relevant view:
"You've reached the 5-workflow limit on Community Edition.
[Upgrade to Starter →] to create unlimited workflows."

`usePlanLimits` composable:
- Loads current usage from /api/v1/workspaces/{id}/usage
- Returns: { workflowsUsed, workflowsLimit, runsUsed, runsLimit, isAtLimit }
- Used by WorkflowListView, InstanceListView to show usage bars

**Unit tests:**
- EditionService: community + 5 workflows → LimitReached=true on 6th
- EditionService: community + 499 runs → allowed, 501 → LimitReached
- PlanLimitException: response body has correct upgrade_url and limit_type

## Technical Requirements
- [ ] Edition limits enforced at service layer (not just frontend)
- [ ] Community edition install: no SSL required (HTTP port 3000)
- [ ] Install script: auto-generates secrets with openssl (never hardcoded defaults)
- [ ] Edition banner: shows only in community edition
- [ ] Upgrade prompt: links to flowamaz.com/pricing

## Acceptance Criteria
- [ ] Community: create 6th workflow → 429 with plan_limit_exceeded error
- [ ] Install script: runs cleanly on macOS and Ubuntu
- [ ] Edition banner visible in community edition sidebar
- [ ] Upgrade link: points to flowamaz.com/pricing

## Output Expected
```
backend/Flowamaz.Application/Platform/Services/EditionService.cs
backend/Flowamaz.Tests.Unit/Platform/EditionServiceTests.cs
infrastructure/community/docker-compose.yml
infrastructure/community/install.sh
infrastructure/community/nginx-community.conf
web/src/composables/usePlanLimits.ts
web/src/components/layout/EditionBanner.vue
```
