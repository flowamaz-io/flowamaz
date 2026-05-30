---
prompt-id: fix-05-01-integration-test-fixes
phase: fix-phase-05
sequence: 1
roles: [Executor, Verifier, Testing]
type: fix
source-issue: Testing — Phase 05 report pre-existing integration failures
severity: Medium
depends-on: []
---

# Fix: Pre-existing Integration Test Failures

## Issues Being Fixed

12 pre-existing integration test failures characterised in phase-05-report.md.
All confirmed as existing before Phase 5 — not regressions.

## Fix 1 — OAuth integration tests need env vars in fixture

In backend/Flowamaz.Tests.Integration/Fixtures/IntegrationApiFixture.cs:
Add test placeholder values for OAuth env vars:

```csharp
Environment.SetEnvironmentVariable("SLACK_CLIENT_ID", "test-slack-client-id");
Environment.SetEnvironmentVariable("SLACK_CLIENT_SECRET", "test-slack-client-secret");
Environment.SetEnvironmentVariable("SLACK_SIGNING_SECRET", "test-slack-signing-secret");
Environment.SetEnvironmentVariable("GITHUB_CLIENT_ID", "test-github-client-id");
Environment.SetEnvironmentVariable("GITHUB_CLIENT_SECRET", "test-github-client-secret");
Environment.SetEnvironmentVariable("MICROSOFT_CLIENT_ID", "test-microsoft-client-id");
Environment.SetEnvironmentVariable("MICROSOFT_CLIENT_SECRET", "test-microsoft-client-secret");
Environment.SetEnvironmentVariable("MICROSOFT_TENANT_ID", "common");
```

These are test-only placeholder values. OAuthService will attempt
real HTTP calls which will fail, so mock the HttpClient in tests:

In Phase4ConnectorTests.cs for OAuth tests:
- Mock IHttpClientFactory to return a test HttpClient
- Test HttpClient handler: return a fake token response for test credentials
- Fake Slack response:
  ```json
  {"ok":true,"access_token":"xoxb-test-token","team":{"id":"T123"},"bot_user_id":"U123"}
  ```
- Fake GitHub response: `access_token=test-github-token&scope=repo&token_type=bearer`
- Fake Microsoft response:
  ```json
  {"access_token":"test-ms-token","refresh_token":"test-refresh","expires_in":3600}
  ```

## Fix 2 — AiNodeWorker integration tests

In backend/Flowamaz.Tests.Integration/Phase4/Phase4AiNodeTests.cs:
The self-correction and sensitive-variable scenarios fail because
the AI stub does not return the expected response format.

Check IAiCompletionService stub in test fixture.
Ensure the stub returns:
- For self-correction test: first call returns invalid JSON, second call returns valid
- For sensitive variable strip test: returns response that does NOT contain the stripped values

Update the mock to handle both scenarios correctly.

## Fix 3 — Validator/workflow create expectation drift

In backend/Flowamaz.Tests.Integration/Phase2/WorkflowIntegrationTests.cs
and backend/Flowamaz.Tests.Integration/Phase3/:

Tests that expect 422 on invalid YAML at POST /workflows:
Update to expect 200 (SFG parser intentionally removed from CreateAsync).

Tests that expect validation to run on create:
Update to call PUT /workflows/{id} + POST /workflows/{id}/validate separately.

Tests for NL generation that expect 200 but get 400:
Check what changed — likely the endpoint URL changed or request body format changed
in the unified creation refactor. Update test to use POST /workflows/create
with { method: "nl", nlRequest: {...} } format.

## Fix 4 — Phase3 DNA/Validate tests

In backend/Flowamaz.Tests.Integration/Phase3/:
Validate_Valid_Yaml — update expected response to match current ValidationResult shape
Validate_Orphaned_Node — confirm BPR/GRF error codes match current validator
Generate_From_NL — update to POST /workflows/create with method:"nl"
DNA tests — check if DNA/Empathy endpoint URL changed to POST (it did in Phase 5)

## Acceptance Criteria
- [ ] dotnet test — all integration tests pass (84/84 target)
- [ ] No new test failures introduced
- [ ] OAuth tests pass with mocked HttpClient
- [ ] Validator tests pass with updated expectations

## Output Expected
```
backend/Flowamaz.Tests.Integration/Fixtures/IntegrationApiFixture.cs
backend/Flowamaz.Tests.Integration/Phase4/Phase4ConnectorTests.cs
backend/Flowamaz.Tests.Integration/Phase4/Phase4AiNodeTests.cs
backend/Flowamaz.Tests.Integration/Phase2/WorkflowIntegrationTests.cs
backend/Flowamaz.Tests.Integration/Phase3/ (updated tests)
```
