---
prompt-id: 07-07-phase7-integration
phase: 07
sequence: 7
roles: [Executor, Verifier, Security, Testing]
type: feature
depends-on: [07-06-performance-production-hardening]
estimated-complexity: Medium
---

# Phase 7 Integration — E2E Tests, Security, Coverage, PHASE_COMPLETE

## Context
All Phase 7 components built. Final prompt: integration tests, E2E, security,
coverage, PHASE_COMPLETE.

## Objective
Phase 7 lifecycle tests, E2E S61-S72, security scan, PHASE_COMPLETE.

## Scope

### What to Build

**Backend integration tests (Phase7/):**

Phase7BillingTests.cs:
- CreateCheckoutSession → returns Stripe URL
- HandleWebhook checkout.session.completed → org plan updated
- HandleWebhook subscription.deleted → org downgraded to Community
- Invalid webhook signature → throws

Phase7AuditTests.cs:
- Create workflow → audit event recorded
- Trigger instance → audit event recorded with actor IP
- Export CSV → correct format and content
- Non-admin → 403

Phase7TemplateTests.cs:
- Install template → WorkflowDefinition created from YAML
- Install increments install_count atomically
- Publish template → submission created
- List templates → returns seeded official templates

Phase7MonitoringTests.cs:
- Replay completed instance → new test instance created
- Replay running instance → 409 Conflict
- Node snapshots captured on execution
- Sensitive variables stripped from snapshots

**Playwright E2E (add to suite):**

billing.spec.ts:
- S61: /pricing renders without auth
- S62: [Upgrade] → redirects to Stripe Checkout URL

audit.spec.ts:
- S63: Create workflow → audit event appears in /settings/audit
- S64: Export CSV → file downloaded

templates.spec.ts:
- S65: Templates tab shows official templates
- S66: Install template → canvas opens with template nodes
- S67: Publish template from workflow detail page

monitoring.spec.ts:
- S68: Instance timeline shows node I/O on expand
- S69: Replay → new test instance created

onboarding.spec.ts:
- S70: First login → product tour starts
- S71: Complete tour → does not show again

performance.spec.ts:
- S72: /health returns healthy with all dependencies

Total E2E: 72 scenarios (S1-S60 + S61-S72)

**Security scan focus:**

- Billing: Stripe webhook signature verified, idempotent processing
- Audit: append-only verified (no UPDATE/DELETE possible via API)
- Templates: YAML injection in template install (YamlDotNet safe deserialization)
- Replay: always creates test instance (isTest enforced)
- Breakpoints: only available in Development (env check)

**Coverage verification (Phase 7 areas):**
- StripeService ≥ 80%
- AuditService ≥ 80%
- TemplateService ≥ 80%
- ReplayService ≥ 80%
- HealthCheckService ≥ 80%

**FmInterpreterPanel v-html resolution:**
If not already resolved: add eslint-disable with security comment.

**Trivy scan:**
Run on all backend dependencies including new Phase 7:
- Stripe.net
- ITfoxtec.Identity.Saml2 (if used in Phase 6)
Expected: 0 Critical/High

**Final checkpoint:**
```
Phase: 07
Next Prompt: PHASE_COMPLETE
Completed: 7
Phase End Status: PHASE_COMPLETE
```

**Production readiness summary:**
Add to phase report: list of items that must be completed before public launch:
1. Stripe account in live mode (currently test mode)
2. SSL certificate (production domain)
3. GITHUB_CONNECTORS_TOKEN set for community submissions
4. AWS Lightsail server provisioned
5. Branch protection rules enabled
6. Staging environment ZAP scan
7. Load test with k6 on critical endpoints

## Acceptance Criteria
- [ ] Phase7 integration tests: all pass
- [ ] S61-S72: all 12 E2E scenarios authored
- [ ] Trivy: 0 Critical/High
- [ ] All Phase 7 coverage areas ≥ 80%
- [ ] Total: unit ≥ 530, integration ≥ 110
- [ ] Production readiness list documented

## Output Expected
```
backend/Flowamaz.Tests.Integration/Phase7/Phase7BillingTests.cs
backend/Flowamaz.Tests.Integration/Phase7/Phase7AuditTests.cs
backend/Flowamaz.Tests.Integration/Phase7/Phase7TemplateTests.cs
backend/Flowamaz.Tests.Integration/Phase7/Phase7MonitoringTests.cs
web/e2e/billing.spec.ts (S61-S62)
web/e2e/audit.spec.ts (S63-S64)
web/e2e/templates.spec.ts (S65-S67)
web/e2e/monitoring.spec.ts (S68-S69)
web/e2e/onboarding.spec.ts (S70-S71)
web/e2e/performance.spec.ts (S72)
checkpoint.md (PHASE_COMPLETE)
results.md (all 7 prompts logged)
```
