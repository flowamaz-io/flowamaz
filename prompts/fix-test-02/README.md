# fix-test-02 — Comprehensive Testing Session (resume point after Phase 08)

Context (audit 2026-10-23):
- Phases 01–08 + fix phases are PHASE_COMPLETE. Last commit 2026-05-31.
- CI was green on develop in May; by October a clean restore failed (new NuGet advisories) — fixed in PR #2.
- The nightly E2E workflow ran 62 times (2026-05-30 → 2026-07-30) and NEVER passed.
  GitHub has since auto-disabled the schedule. Two harness defects make it impossible to pass:
    1. `sleep 20` after a cold `dotnet run` — restore+build (plus startup migrations, which
       Program.cs applies on boot) takes longer, so the API is not up when tests start.
    2. `web/vite.config.ts` has no `/api` proxy while the app uses relative `/api/...` URLs
       (VITE_API_BASE_URL is empty) → every browser API call 404s on the Vite dev server.
  (Correction 2026-10-23: an earlier version of this note also blamed missing migrations —
  wrong; StartupMigrationService migrates on boot.)
  Conclusion: S1–S80 have never genuinely executed. Their pass rate is unknown.

Run order (one prompt per session, /clear between):
1. test-01-backend-verify.md     — reproduce CI locally, confirm counts, record baseline
2. test-02-e2e-harness.md        — fix the harness defects, make the suite RUN (not pass)
3. test-03-e2e-triage.md         — classify every failure; no fixes yet
4. test-04-e2e-fix.md            — fix by priority until suite is green (repeatable)
5. test-05-hardening-travel.md   — close Phase 08 travel-forward items L1–L3

Rules for this whole session:
- Never weaken, skip, or delete an assertion to make a test pass. If a test is wrong,
  say why in results.md and fix the test to match FUNCTIONAL.md, not the current code.
- Every prompt appends to results.md and updates checkpoint.md.
- Commit per prompt: `test(e2e): ... [fix-test-02-NN]`, push to develop.
