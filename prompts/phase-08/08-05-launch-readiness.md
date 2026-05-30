---
prompt-id: 08-05-launch-readiness
phase: 08
sequence: 5
roles: [Executor, Verifier, Security]
type: feature
depends-on: [08-04-multi-workspace]
estimated-complexity: Medium
---

# Launch Readiness — Staging, Load Tests, Final Hardening

## Context
The production readiness checklist from Phase 7 identified items needed before
public launch. This prompt addresses the code and configuration items that can
be done without infrastructure access.

## Objective
Complete all code-side launch readiness items: load test scripts, staging
configuration, security headers, rate limit tuning, and final env var audit.

## Scope

### What to Build

**k6 load test scripts (k6/ folder):**

k6/trigger-workflow.js:
```javascript
// Simulates 50 concurrent users triggering workflows
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  vus: 50,
  duration: '2m',
  thresholds: {
    http_req_duration: ['p95<500'],  // 95% of requests under 500ms
    http_req_failed: ['rate<0.01'],  // Less than 1% failure rate
  },
};

export default function () {
  const res = http.post(
    `${__ENV.BASE_URL}/api/public/v1/workflows/${__ENV.WORKFLOW_SLUG}/trigger`,
    JSON.stringify({ test: true }),
    { headers: { 
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${__ENV.API_KEY}` 
    }}
  );
  check(res, { 'status is 202': (r) => r.status === 202 });
  sleep(1);
}
```

k6/auth-load.js — login endpoint under load (10 VUs, 1 min)
k6/copilot-load.js — co-pilot endpoint (5 VUs, 1 min, semantic cache should absorb most)
k6/instance-status.js — polling instance status (100 VUs, 2 min)

docs/LOAD-TESTING.md:
```markdown
# Load Testing with k6

## Prerequisites
brew install k6

## Run against local stack
BASE_URL=https://localhost:8443 API_KEY=... WORKFLOW_SLUG=... \
  k6 run k6/trigger-workflow.js

## Expected results
p95 < 500ms, error rate < 1%
```

**Staging docker-compose:**

infrastructure/docker-compose.staging.yml:
Same as production but:
- ASPNETCORE_ENVIRONMENT=Staging
- Adds OWASP ZAP service for automated scanning
- Reduced resource limits (staging is smaller)
- All same secrets via env (no defaults)

infrastructure/staging.env.example:
All env vars needed for staging with staging-specific placeholders.

**Security headers audit:**

In infrastructure/nginx/nginx.conf:
Verify and add missing security headers:

```nginx
add_header Content-Security-Policy "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; font-src 'self' data:; connect-src 'self' https://api.anthropic.com https://api.resend.com;" always;
add_header Permissions-Policy "camera=(), microphone=(), geolocation=()" always;
add_header Cross-Origin-Opener-Policy "same-origin" always;
add_header Cross-Origin-Resource-Policy "same-origin" always;
```

Verify existing headers are correct:
- X-Frame-Options: SAMEORIGIN ✓ (already in Phase 1)
- X-Content-Type-Options: nosniff ✓
- Strict-Transport-Security: max-age=31536000 ✓
- Referrer-Policy: strict-origin-when-cross-origin ✓

**Rate limit tuning:**

Review rate limits set in Phase 1 nginx.conf and Phase 2-6 Redis rate limiters.
Update to production-appropriate values:

nginx.conf:
- auth zone: 10r/m → keep (brute force protection)
- api zone: 100r/m → 200r/m (more generous for legitimate use)
- Add webhook zone: 1000r/m (webhooks can be high-volume)

Backend rate limiters:
- Forgot password: 3/hour → keep
- Webhook receive: 100/min per endpoint → keep
- Public API: 1000/hour → keep
- Co-pilot: add 20/hour per workspace (AI cost protection)

**Final environment variable audit:**

Update .env.example with every env var used across all phases:
Group by section: Database, Redis, JWT, Security, Email, AI, Git,
Stripe, GitHub, OAuth, SSO, Platform, Edition, Rate Limits.

Each var: placeholder value + one-line comment explaining what it is.

Verify Program.cs startup validation covers all Critical vars:
DB_CONNECTION_STRING, JWT_SECRET, CREDENTIAL_MASTER_KEY, GATE_SIGNING_KEY,
Email__ResendApiKey, Ai__AnthropicPlatformKey, STRIPE_SECRET_KEY (if billing active).

**CORS final review:**

In Program.cs CORS configuration:
In Production: only allow PLATFORM_BASE_URL origin (no localhost).
In Development: allow localhost:8306, localhost:8443, localhost:5173.
Add MARKETING_SITE_URL to allowed origins (flowamaz.com needs to call the API for the pricing page).

Unit tests:
- Rate limit: co-pilot > 20/hour per workspace → 429
- CORS: Production origin only
- Security headers: all required headers present in nginx config

After all:
dotnet build — 0 errors
npm run build --prefix web — 0 errors
git add . && git commit -m "feat(launch): k6 load tests, staging config, security headers, rate limits, env audit" && git push origin develop
