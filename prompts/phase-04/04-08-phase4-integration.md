---
prompt-id: 04-08-phase4-integration
phase: 04
sequence: 8
roles: [Executor, Verifier, Security, Testing]
type: feature
depends-on: [04-07-connector-payload-mapper]
estimated-complexity: Medium
---

# Phase 4 Integration — E2E Tests, Coverage, PHASE_COMPLETE

## Context
All Phase 4 components built. Final prompt: integration tests, E2E scenarios,
coverage ≥ 80%, security scan, PHASE_COMPLETE.

## Objective
Phase 4 lifecycle tests, Playwright E2E for connector flows and gates,
coverage verification, Trivy rescan, PHASE_COMPLETE.

## Scope

### What to Build

**Backend integration tests (Phase4/):**

Phase4ConnectorTests.cs:
- Install connector → WorkspaceConnector row created
- OAuth initiate → state stored in Redis with 10-min TTL
- OAuth callback valid state → credential stored encrypted
- OAuth callback expired state → 400 Bad Request
- GET /connectors/health → expired credential shows expired status
- POST /auto-map with matching JSON → high confidence mapping returned

Phase4GateTests.cs:
- Trigger workflow with HumanGate → instance status=Waiting, GateDecision=Pending
- POST /gates/{id}/decide approve → instance resumes, status=Running
- POST /gates/{id}/decide reject → instance fails, saga triggered
- Email link valid HMAC → gate decided, redirect to success
- Email link expired HMAC → 400

Phase4AiNodeTests.cs:
- AI node with sensitive variable → mock proves sensitive value NOT in AI prompt
- AI node with output schema → invalid response → self-correction retry attempted
- AI node with cost cap → estimated cost > cap → exception before API call

**Playwright E2E:**

connectors.spec.ts:
- S34: /library → 13 connector cards visible
- S35: Click "Install & Connect" → CredentialSetupWizard opens to step 1
- S36: /library/health → health table renders with status badges

gates.spec.ts:
- S37: /gates → pending gates table renders
- S38: Approve gate from portal → toast confirms, gate removed from list

empathy.spec.ts:
- S39: Workflow editor → [Empathy] toggle → EmpathyPanel visible

Total E2E: 39 scenarios (S1-S33 + S34-S39)

**Coverage check (Phase 4 areas):**
- Application/Connectors/* ≥ 80%
- Application/Workflow/Workers/* ≥ 80%
- Application/Workflow/Empathy/* ≥ 80%

**Security scan:**
Trivy rescan (Phase 4 adds: Npgsql, MySqlConnector, OpenXml, PdfPig).
Static — connector-specific:
- Credential values: never in any log (verify grep on all handler classes)
- OAuth state: Redis TTL confirmed (10 min)
- Email approval links: HMAC key from env var, never hardcoded
- SQL connectors: parameterised queries only (no raw string interpolation)

**checkpoint.md:**
```
Phase: 04
Next Prompt: PHASE_COMPLETE
Completed: 8
Phase End Status: PHASE_COMPLETE
```

## Technical Requirements
- [ ] Phase4ConnectorTests: all pass (real Redis for OAuth state tests)
- [ ] Phase4GateTests: gate lifecycle complete
- [ ] Phase4AiNodeTests: sensitive variable NOT in prompt (proven by mock)
- [ ] S34-S39: all 6 new Playwright scenarios pass
- [ ] Phase 4 service coverage ≥ 80% all areas
- [ ] Trivy: 0 Critical/High
- [ ] SQL connectors: grep for string concatenation in queries → 0 results

## Acceptance Criteria
- [ ] OAuth full flow: initiate → callback → credential in vault (integration test)
- [ ] Gate approve: instance resumes (integration test)
- [ ] S34-S39: all pass
- [ ] Coverage: Connectors, Workers, Empathy all ≥ 80%
- [ ] Trivy: 0 Critical/High

## Output Expected
```
backend/Flowamaz.Tests.Integration/Phase4/Phase4ConnectorTests.cs
backend/Flowamaz.Tests.Integration/Phase4/Phase4GateTests.cs
backend/Flowamaz.Tests.Integration/Phase4/Phase4AiNodeTests.cs
web/e2e/connectors.spec.ts (S34-S36)
web/e2e/gates.spec.ts (S37-S38)
web/e2e/empathy.spec.ts (S39)
checkpoint.md (PHASE_COMPLETE)
results.md (all 8 prompts logged)
```
