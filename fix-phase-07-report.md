# Fix Phase 07 — Report

**Phase:** fix-phase-07 — Phase 7 Fix (Template Format + DTO Validation + Audit + Polish)
**Date:** 2026-05-30
**Status:** ⚠️ PARTIAL — 3 of the highest-severity items fixed, verified and committed; remaining medium/low items deferred (see §5).
**Source prompts:** `prompts/fix-phase-07/fix-07-01-critical-high.md`, `prompts/fix-phase-07/fix-07-02-medium-low.md`

> Honesty note: every "verified" claim below corresponds to actual `dotnet build`/`dotnet test` output observed during execution. Items marked *deferred* were not implemented. Commits are **local only — not yet pushed to `origin/develop`** (see §6).

---

## 1. Summary

Phase 7 shipped Billing/Stripe, the template gallery, advanced monitoring (replay + dev breakpoints), onboarding and production hardening. The Verifier flagged a **Critical** template-publish blocker plus several High/Medium/Low items. This fix phase addressed the Critical blocker and the two highest-value High items first, each verified green and committed independently.

The biggest finding during execution: **several prompt-described issues were already resolved in the shipped code** (e.g. dev breakpoint endpoints already 404 outside Development; `instance.started` already recorded `ActorType`). The prompts were written against assumptions that did not all match the real codebase, so each item was re-scoped against the actual code before fixing.

---

## 2. Items fixed (verified + committed)

### 2.1 CRITICAL — Template YAML format unification — `6dbae64`
**Problem:** `Flowamaz.Core/Workflow/SfgParser.cs` read only root-level `workflow:`/`nodes:`/`edges:`, but every seeded/exported template uses the documented `flowamaz/v1` envelope (`apiVersion`/`kind`/`metadata:`/`spec.nodes`/`spec.edges`). Result: installing, publishing or triggering any template failed with `SfgParseException`.

**Fix:**
- `SfgParser` now reads `spec.nodes`/`spec.edges` when present, falling back to root-level for hand-authored/legacy workflows.
- Metadata read from the `metadata:` block or the legacy `workflow:` block.
- Hyphenated node-type names normalised so the v1 schema parses (`human-gate`→`HumanGate`, `if-else`→`IfElse`, etc.), matching the `NodeType` enum.
- Added unit test `SfgParserTests.Parse_spec_envelope_reads_nodes_edges_and_hyphenated_types`.

Both runtime paths route through `SfgParser` (`WorkflowService.PublishAsync`, `WorkflowOrchestrator.TriggerAsync` via `LoadGraphAsync`), so install→publish→trigger of templates now works end-to-end.

**Verification:** `dotnet build Flowamaz.Core` → 0 warnings / 0 errors; SfgParser tests **8/8 pass**.

**Files:** `backend/Flowamaz.Core/Workflow/SfgParser.cs`, `backend/Flowamaz.Tests.Unit/Workflow/SfgParserTests.cs`

### 2.2 HIGH — `instance.started` audit actor — `5f89fb8`
**Problem:** the `instance.started` audit event recorded `ActorType` but no `actor_user_id`.

**Fix:**
- `IWorkflowOrchestrator.TriggerAsync` / `WorkflowOrchestrator` gain an optional `Guid? triggeredByUserId`.
- The audit event now sets `ActorUserId = triggeredByUserId` and `ActorType = triggeredByUserId.HasValue ? "user" : "system"` (human API triggers → user; webhook/API-key/scheduled → system).
- `WorkflowInstancesController.Trigger` passes the authenticated `_currentUser.UserId`.
- Updated the three `WebhookServiceTests` Moq setups for the new parameter (the webhook path passes no user, so it remains `system`).

**Verification:** `dotnet test` (WebhookServiceTests + WorkflowOrchestratorTests) → **20/20 pass**, build 0/0.

**Files:** `backend/Flowamaz.Core/Interfaces/Workflow/IWorkflowOrchestrator.cs`, `backend/Flowamaz.Application/Workflow/Orchestrator/WorkflowOrchestrator.cs`, `backend/Flowamaz.Api/Controllers/WorkflowInstancesController.cs`, `backend/Flowamaz.Tests.Unit/Webhooks/WebhookServiceTests.cs`

### 2.3 Dev-endpoint hardening + Replay 404 + cleanup — `582e270`
**Fix (fix-07-02 FIX 5 + fix-07-01 cleanup 6 + 4):**
- New `DevelopmentOnlyFilter` (`IActionFilter`, registered in DI) applied to the `/api/v1/dev` breakpoint/resume/step endpoints — they return 404 outside Development at the filter layer, layered on top of the existing per-action environment guards (defense-in-depth). Applied at *method* level, not controller level, because the same controller also hosts the production-available Replay endpoint.
- Replay endpoint now returns an actionable 404 body (`instance_not_found` + guidance that only terminal instances can be replayed) instead of a bare `NotFound()`.
- Removed the stray `.env copy.example`.

**Verification:** full unit-test-project build → **0 warnings / 0 errors**.

**Files:** `backend/Flowamaz.Api/Filters/DevelopmentOnlyFilter.cs` (new), `backend/Flowamaz.Api/Controllers/InstanceDebugController.cs`, `backend/Flowamaz.Api/Program.cs`, deleted `.env copy.example`

---

## 3. Items found already resolved (no change needed)
- **Dev breakpoint/resume/step endpoints** already returned 404 outside Development via per-action `IsDevelopment()` guards (filter added as defense-in-depth — §2.3).
- **`instance.started` `ActorType`** was already recorded; only `actor_user_id` was missing (§2.2).
- The **six-layer `WorkflowValidator`** already reads `spec.nodes` correctly; only the runtime `SfgParser` was out of step (§2.1).

---

## 4. Verifier / Security / Testing (this phase)

- **Verifier:** Confirmed the SfgParser format unification (spec + legacy both parse; node-type normalisation correct against the `NodeType` enum). DTO-validator review pending (see deferred §5).
- **Security:** Stripe redirect-URL allowlist is **deferred** (§5). Dev controller is gated both by route-level filter and per-action guards (§2.3). Credential/workspace-isolation rules unaffected by these changes.
- **Testing:** SfgParser 8/8; WebhookService + Orchestrator 20/20; full unit-project builds clean. Full-suite + integration (`Phase7*Tests`) run **pending** — recommend running before push (§6). The prompt's install→publish→trigger integration test in `Phase7TemplateTests.cs` was **not** added (needs the live `IntegrationApiFixture`).

---

## 5. Deferred items (not implemented)

**fix-07-01**
- **FIX 2 — 6 DTO validators + Stripe redirect-URL allowlist.** The records (`CheckoutRequest`, `PortalRequest`, `InstallTemplateRequest`, `PublishTemplateRequest`, `ReplayRequest`, `SetBreakpointRequest`) live in the **API** project and are validated *manually*. Resume by: creating validators in `Flowamaz.Api`, registering them, injecting `IValidator<T>` into `BillingController`/`TemplatesController`/`InstanceDebugController`, and calling `ValidateAndThrowAsync` (pattern: `WorkflowInstancesController`). Stripe allowlist: validate `SuccessUrl`/`CancelUrl`/`ReturnUrl` host against `Platform:BaseUrl` (+ `localhost` in Development).
- **CLEANUP 5** — `TemplateException : AppException` (404/422/400) replacing `InvalidOperationException` in `TemplateService` (+ update `TemplateServiceTests`).
- **CLEANUP 7** — workspace-membership check on the dev endpoints.

**fix-07-02**
- **FIX 1 — breakpoint auto-pause in `StepAsync`.** Needs adaptation: the orchestrator has **no SignalR/`_hubContext`** the prompt assumes. Building blocks present: `IBreakpointRegistry` (in-memory) + `IStepDebuggerService`; add a pre-node check in `StepAsync` and a `BreakpointHit` status.
- **FIX 2/3/4 (frontend)** — `Sidebar.vue` gates tooltip, `GatesView.vue` empty-state CTA, `ProductTour.vue` steps 4–5 copy, `FmTooltip.vue` hover-keep-open, then `npm run build`. (All four files confirmed to exist.)

**Phase-end** — Verifier deep review, Security scan (Trivy), Testing Agent full run, and the notification hook were not run in full.

---

## 6. Notes / follow-ups
- **Commits are local only — not pushed.** Three commits sit on `develop` locally: `6dbae64`, `582e270`, `5f89fb8`. Run a full `dotnet test` (unit + integration) to confirm, then push.
- `checkpoint.md` still reflects fix-07-01 in progress; update it when the phase truly completes.
- Execution ran under degraded tool-output delivery; each verification was confirmed from real build/test output before the corresponding commit.
