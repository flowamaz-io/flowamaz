Read CLAUDE.md, FUNCTIONAL.md, checkpoint.md, e2e-triage-report.md (including human notes).

GOAL: Drive the E2E suite to green, in severity order. This prompt is REPEATABLE — run it
again in a fresh session until checkpoint shows E2E_GREEN.

Loop:
1. Take the highest-severity open item from e2e-triage-report.md that is not marked
   OUT-OF-SCOPE by the human.
2. APP-BUG  → fix product code, add/extend a unit or integration test that reproduces it first.
   TEST-BUG → fix the test to match FUNCTIONAL.md; justify in the report.
   SEED/ENV → add deterministic seed/fixture or mock at the HTTP boundary; never call live
              Stripe/AI/vendor APIs from E2E — use the existing stub modes and assert stub mode.
   NOT-BUILT in scope → build it to FUNCTIONAL.md, with tests.
   FLAKY    → fix the race (await the real condition); never add fixed waits/sleeps.
3. Re-run the affected spec, then the full suite every 5 fixes.
4. Mark the item FIXED in the report with commit hash.
5. dotnet build 0/0, dotnet test green, web build + vitest green before each commit.
   Commit per item: fix(<scope>): <what> [S<nn>]

Stop conditions:
- Full suite green twice in a row locally AND once in CI (gh workflow run E2E --ref develop)
  → checkpoint Current Prompt: test-05-hardening-travel, Notes: E2E_GREEN.
- Context running low → update checkpoint with the next open item and stop cleanly.

Forbidden: test.skip, test.fixme, loosening an assertion, raising timeouts above 15s to hide
slowness, deleting a scenario. If you believe a scenario should be removed, mark it
PROPOSED-REMOVAL in the report for the human.
