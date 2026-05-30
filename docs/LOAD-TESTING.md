# Load Testing

Load tests for the performance-critical endpoints, written for [k6](https://k6.io). Scripts live in
`k6/` at the repo root. They exercise the four hot paths: public workflow trigger, login, Co-pilot,
and instance status polling.

## Prerequisites

```bash
brew install k6        # macOS
# or: see https://k6.io/docs/get-started/installation/ for Linux/Windows
```

Run against either:
- **Local stack** — `infrastructure/docker-compose.yml` up, `BASE_URL=http://localhost:8306`.
- **Staging stack** — `infrastructure/docker-compose.staging.yml` up, `BASE_URL=https://staging.flowamaz.io`.

You need valid credentials/tokens for the target environment (a seeded login, a JWT for an
authenticated user, a public API key, and an existing workflow slug + instance id).

## Scripts

Set the shared `BASE_URL`, then run each script with its env vars. Each prints a summary with the
p95 latency and error rate; the thresholds baked into the scripts fail the run if they regress.

```bash
export BASE_URL=http://localhost:8306

# 1. Public workflow trigger — 50 VUs, 2m, expects 202
k6 run -e BASE_URL=$BASE_URL -e WORKFLOW_SLUG=my-workflow -e API_KEY=fka_live_xxx \
  k6/trigger-workflow.js

# 2. Login — 10 VUs, 1m, expects 200
k6 run -e BASE_URL=$BASE_URL -e LOGIN_EMAIL=user@example.com -e LOGIN_PASSWORD=secret \
  k6/auth-load.js

# 3. Co-pilot — 5 VUs, 1m, expects 200 (semantic cache absorbs most calls)
k6 run -e BASE_URL=$BASE_URL -e JWT=eyJhbGc... \
  k6/copilot-load.js

# 4. Instance status polling — 100 VUs, 2m, expects 200
k6 run -e BASE_URL=$BASE_URL -e JWT=eyJhbGc... -e INSTANCE_ID=<guid> \
  k6/instance-status.js
```

## Environment variables per script

| Script | Required env vars |
|--------|-------------------|
| `trigger-workflow.js` | `BASE_URL`, `WORKFLOW_SLUG`, `API_KEY` |
| `auth-load.js` | `BASE_URL`, `LOGIN_EMAIL`, `LOGIN_PASSWORD` |
| `copilot-load.js` | `BASE_URL`, `JWT` |
| `instance-status.js` | `BASE_URL`, `JWT`, `INSTANCE_ID` |

- `API_KEY` — a public API key for the workspace (sent as `Authorization: Bearer`).
- `JWT` — an access token from `POST /api/v1/auth/token` for an authenticated user.
- `WORKFLOW_SLUG` — slug of a published workflow with a public trigger enabled.
- `INSTANCE_ID` — GUID of an existing workflow instance to poll.

## Expected results

All scripts should report:

- **p95 latency < 500ms** (`http_req_duration` p(95)).
- **Error rate < 1%** (`http_req_failed` rate < 0.01) — enforced on `trigger-workflow.js` and
  `instance-status.js`.
- **All checks passing** — status `202` for trigger, `200` for the others.

If thresholds fail, k6 exits non-zero. Investigate the slowest endpoint first: the trigger path must
stay free of heavy work, instance status must be served from cache, and Co-pilot must hit the
semantic cache before any AI call. A high Co-pilot error/latest-latency usually means the cache is
cold or pattern matching is missing the command.
