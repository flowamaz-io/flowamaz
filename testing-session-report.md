# Testing Session Report — fix-test-02 (23 Oct → 13 Nov 2026)

Resumed after a five-month pause with Phases 01–08 complete. The session's prompts (prompts/fix-test-02/) were
carried out as reviewed PRs into `develop` rather than direct pushes, with a human browser check (Claude Code on
Jagan's Mac) after each PR.

## Baseline (test-01)
- CI had gone red: a clean restore failed on new NuGet advisories (fixed in PR #2).
- The nightly E2E workflow had run 62 times (30 May → 30 Jul) and never passed; GitHub had disabled the schedule.
  Cause: harness defects (`sleep 20` before a cold API start, no Vite `/api` proxy), so S1–S80 had never really run.
- A code-reading audit (prompts/fix-test-02/feature-depth-audit.md) found features that were shallow or missing
  behind passing unit tests: connectors could not run as workflow steps, Slack/GitHub auth placeholders, schedule
  triggers never fired, invalid AI YAML was saved, a dead voice tab.

## Harness (test-02)
- Vite proxies `/api`, `/webhooks`, `/scalar` to the API; readiness waits replace `sleep`.
- One Playwright config (`web/e2e/playwright.config.ts`; list + HTML + JSON reporters).
- `scripts/e2e-local.sh` runs the suite on its own stack (API 8317, web 5183, marketing 5184, `flowamaz_e2e`
  database, per-run keys); `.github/workflows/e2e.yml` runs the same script nightly and uploads the reports. (PR #20)

## Triage and fixes (test-03, test-04)
First full run of the suite once it could run: **36 passed, 44 failed**. Classified as:
- **Stale tests** (most failures): selectors and copy that had drifted from the UI, setups missing the data a page
  needs, the first-visit product tour covering the page. Fixed in the specs; no assertion was weakened.
- **App bugs**, fixed: canvas loaded engine-spelled node types (`HumanGate`) as unknown, so gate settings never
  showed; `fmz connector …` commands documented but missing; Library tabs, ROI labels and checklist steps not
  reachable by keyboard / screen reader.
- **Not built / shallow** (from the audit), built across PRs #2–#19: connectors as steps with vault credentials,
  sandboxed third-party connectors, schedule triggers, Workflow Economy (Stripe test mode), canvas box select /
  minimap / YAML jump / grid snap, creation review steps, gate timeouts, Enterprise SSO and SCIM, public API limits
  by plan, session revocation, Empathy from real runs, DNA inheritance, audit PDF, Conversation→Workflow review.

## Hardening (test-05)
- **L1** redirect allowlist compares scheme and port as well as host — PR #19.
- **L2** last admin leave/remove/demote race — advisory lock per workspace on all three paths (PR #15), with
  concurrent tests for demotion (PR #15) and leaving (this PR).
- **L3** Co-pilot user and workspace caps checked together, atomically — PR #19.

## Final state
| Suite | Result |
|---|---|
| Backend unit | 1,068 / 1,068 |
| Backend integration | 187 pass locally; 17 need Docker (Testcontainers) and run in CI; 1 skipped |
| Web unit (vitest) | all pass |
| E2E (Playwright, S1–S81) | 81 pass, 0 fail, 1 skipped (S46 needs a Community-edition stack) — locally and in CI |
| Build | backend 0 errors / 0 warnings; web and marketing build |

## Open scope decisions
See `docs/ROADMAP-YEAR2-3.md`: four in-scope gaps found at the end of the session (the `fmz library` commands call
routes that don't exist; connector version pinning; Library Forms; Library Packs), then the Year 2/3 items, sized,
with what each needs from a person.

Before production (needs a person or real accounts): Stripe test-mode walk-through, `docker compose up` with the
connector sandbox, a browser check of canvas box select and minimap drag, `Platform__StaffOrgIds`,
`CONNECTOR_RUNNER_SECRET`, one backend image build, and backing up workspace Git repos before the first deploy with
the new volume.
