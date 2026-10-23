Read CLAUDE.md, checkpoint.md, phase-08-report.md ("Travel Forward").

Close the three Phase 08 travel-forward items. Test first for each.

--- L1: Redirect allowlist compares host only ---
RedirectUrlPolicy.IsAllowed: also require scheme == https (http allowed only when
IsDevelopment()) and port equal to the configured base URL's port (default ports normalised).
Tests: https→http downgrade rejected; same host different port rejected; default port 443
explicit vs implicit both accepted.

--- L2: Last-admin race on leave/remove ---
LeaveWorkspaceAsync and RemoveMemberAsync use read-then-write on CountActiveAdminsAsync.
Make it atomic: run the count + update in a SERIALIZABLE transaction with retry on
serialization failure (or SELECT ... FOR UPDATE on the workspace's admin member rows).
Integration test: two admins of a 2-admin workspace leave concurrently (Task.WhenAll) →
exactly one succeeds, the other gets 409 LastAdminException, workspace keeps one admin.

--- L3: Co-pilot cap order ---
Check the per-workspace cap BEFORE incrementing the per-user counter, so a workspace-capped
request does not consume the user's slot. Unit test.

Run full backend tests + E2E suite. results.md entry; checkpoint → Phase: TESTING_COMPLETE.
Compile testing-session-report.md (baseline, harness fixes, triage counts, fixes, final
pass rates, open scope decisions). Trigger .claude/hooks/notify-phase-complete.sh. Stop.
Commit: fix(security): travel-forward L1–L3 + testing session report [fix-test-02-05]
