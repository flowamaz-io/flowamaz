---
prompt-id: 03-08-phase3-integration
phase: 03
sequence: 8
roles: [Executor, Verifier, Security, Testing]
type: feature
depends-on: [03-07-workflow-weather-dna]
estimated-complexity: Medium
---

# Phase 3 Integration — E2E Tests, Coverage, PHASE_COMPLETE

## Context
All Phase 3 components built. Final prompt: integration tests, E2E scenarios,
coverage, security scan, and PHASE_COMPLETE.

## Objective
Phase 3 lifecycle integration tests, Playwright E2E for all new views and creation
methods, coverage ≥ 80% on all Phase 3 service classes, Trivy rescan.

## Scope

### What to Build

**Backend integration tests (Phase3/):**

Phase3CreationTests.cs:
- POST /generate: NL→YAML returns valid YAML (mock AI)
- POST /generate: same request twice → second returns cached:true
- POST /validate: valid YAML → IsValid=true
- POST /validate: orphaned node → Layer 2 error in response
- POST /validate: hardcoded secret pattern → Layer 5 error
- POST /copilot: matched pattern → no AI call (verify via mock)
- POST /copilot: rate limit exceeded (61st call) → 429
- POST /from-conversation: Slack text → YAML with human-gate

Phase3DnaTests.cs:
- Two workflows with same connectors → similarity > 70
- POST /clone → new draft workflow with same YAML
- Clone appears in workflow list

**Playwright E2E (add to suite):**

editor.spec.ts (new scenarios):
- S26: Navigate to /workflows/new → 6 creation method cards all active (no "coming soon")
- S27: NL template form → fill all 6 sections → submit → canvas shows workflow
- S28: Canvas → drag node from palette → node appears on canvas
- S29: Canvas → Ctrl+K → Co-pilot panel opens → type "add timeout" → patch applied
- S30: Canvas → right-click node → "Help with this node type" → help panel opens

weather.spec.ts:
- S31: /weather → workflow cards visible with coloured status dots
- S32: Click insight bell → insights sidebar opens

creation.spec.ts:
- S33: /workflows/new → type name similar to existing workflow → clone suggestion appears

Total E2E: 33 scenarios (S1-S25 from phases 1-2 + S26-S33 Phase 3)

**Coverage check:**
Phase 3 service classes (Coverlet):
- Application/Workflow/Creation/* — target ≥ 80%
- Application/Workflow/Validation/* — target ≥ 80%
- Application/Workflow/Dna/* — target ≥ 80%

**Security scan:**
Run Trivy on rebuilt images (Phase 3 adds: cytoscape, iText/PdfPig, OpenXml).
Static scan — Phase 3 specific:
- Visual input: image bytes never logged
- SOP text: document content not logged (may contain PII)
- Co-pilot commands: not logged at Info level (user commands are personal data)
- DNA hashes: no workflow content in hash input (only structural metadata)

**checkpoint.md final:**
```
Phase: 03
Next Prompt: PHASE_COMPLETE
Completed: 8
Phase End Status: PHASE_COMPLETE
```

## Technical Requirements
- [ ] Phase3CreationTests: all pass (mock AI, real Redis for cache tests)
- [ ] All 8 new Playwright scenarios pass (S26-S33)
- [ ] Phase 3 service coverage ≥ 80% all areas
- [ ] Trivy: 0 Critical, 0 High after Phase 3 NuGet additions
- [ ] SOP text not logged at any level above Debug
- [ ] Co-pilot command text not logged at Info level
- [ ] checkpoint.md: PHASE_COMPLETE

## Acceptance Criteria
- [ ] POST /generate: mock AI → valid YAML returned
- [ ] POST /copilot: 61st call → 429 (integration test, real Redis rate limit)
- [ ] S26-S33: all 8 new E2E scenarios pass
- [ ] Coverage: Creation, Validation, Dna — all ≥ 80%
- [ ] Trivy: 0 Critical/High on rebuilt images

## Output Expected
```
backend/Flowamaz.Tests.Integration/Phase3/Phase3CreationTests.cs
backend/Flowamaz.Tests.Integration/Phase3/Phase3DnaTests.cs
web/e2e/editor.spec.ts (S26-S30)
web/e2e/weather.spec.ts (S31-S32)
web/e2e/creation.spec.ts (S33)
checkpoint.md (PHASE_COMPLETE)
results.md (all 8 prompts logged)
```
