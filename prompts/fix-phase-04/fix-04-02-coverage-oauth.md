---
prompt-id: fix-04-02-coverage-oauth
phase: fix-phase-04
sequence: 2
roles: [Executor, Verifier, Testing]
type: fix
source-issue: Verifier/Coverage + Feature — Phase 04 report
severity: Major
depends-on: [fix-04-01-security-hardening]
---

# Fix: Coverage Gaps + Real OAuth Token Exchange

## Issues Being Fixed

```
M2 — ConnectorCatalogueService at 0% unit test coverage
FINDING: Install/Uninstall/GetAll/GetById/GetInstalled all uncovered.
FIX REQUIRED: ConnectorCatalogueServiceTests.cs
```

```
M3 — Infrastructure coverage 8.2% (ConnectorSandbox, ByomProviderService at 0%)
FIX REQUIRED: Unit tests for ConnectorSandbox and ByomProviderService.
```

```
M4 — OAuth token exchange is a stub (mock_token_{code})
FINDING: Slack and GitHub OAuth connectors cannot authenticate in production.
FIX REQUIRED: Real token exchange for Slack and GitHub minimum.
```

## Objective
Close coverage gaps on Phase 4's most important new services. Implement real OAuth
token exchange for Slack and GitHub. Application must be fully testable after this prompt.

## Scope

### Fix 1 — ConnectorCatalogueServiceTests

Tests to write (Flowamaz.Tests.Unit/Connectors/ConnectorCatalogueServiceTests.cs):

- GetAllAsync_ReturnsAllSeededConnectors
  Mock repo returns 13 connectors. Verify count = 13.

- GetByIdAsync_ExistingConnector_ReturnsConnector
  Mock returns one connector. Verify correct ID returned.

- GetByIdAsync_UnknownConnector_ReturnsNull

- GetInstalledAsync_WorkspaceWithInstalledConnectors_ReturnsInstalled
  Mock returns 2 WorkspaceConnector rows. Verify 2 returned.

- InstallAsync_NotAlreadyInstalled_CreatesWorkspaceConnector
  Verify WorkspaceConnector created with correct WorkspaceId + ConnectorDefinitionId.

- InstallAsync_AlreadyInstalled_ThrowsOrReturnsExisting
  Verify idempotent behaviour.

- UninstallAsync_Installed_RemovesWorkspaceConnector
  Verify WorkspaceConnector soft-deleted or removed.

- UninstallAsync_NotInstalled_ThrowsNotFoundException

### Fix 2 — ConnectorSandboxTests

Tests to write (Flowamaz.Tests.Unit/Connectors/ConnectorSandboxTests.cs):

- ExecuteAsync_ValidInput_BuildsCorrectHttpRequest
  Mock IHttpClientFactory. Verify method, URL, headers, body match connector manifest.

- ExecuteAsync_InputFailsSchemaValidation_ThrowsValidationException
  Pass input missing a required field. Verify exception before any HTTP call.

- ExecuteAsync_HttpReturns500_ReturnsErrorResult
  Mock HttpClient to return 500. Verify ConnectorExecutionError with status code.

- ExecuteAsync_HttpReturns200_ReturnsTypedOutput
  Mock returns valid JSON. Verify output matches output_schema.

- ExecuteAsync_NetworkTimeout_ReturnsTimeoutError
  Mock HttpClient to throw TaskCanceledException. Verify timeout error result.

### Fix 3 — ByomProviderServiceTests

Tests to write (Flowamaz.Tests.Unit/Connectors/ByomProviderServiceTests.cs):

- CompleteAsync_ValidByomEndpoint_SendsOpenAiCompatibleRequest
  Mock HttpClient. Verify request body has "model", "messages" fields.
  Verify Authorization header contains BYOM credential value.

- CompleteAsync_ByomReturns401_ThrowsAuthenticationException

- CompleteAsync_ResponseParsed_ReturnsContentText
  Mock returns OpenAI-compatible response. Verify extracted text content returned.

### Fix 4 — Real OAuth Token Exchange

Implement real token exchange in OAuthService for Slack and GitHub.
These two cover the most common OAuth connectors.

**Slack OAuth token exchange:**
```csharp
case "slack":
    var slackResponse = await httpClient.PostAsync(
        "https://slack.com/api/oauth.v2.access",
        new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = config["Slack:ClientId"] ?? throw new InvalidOperationException("Slack:ClientId not configured"),
            ["client_secret"] = config["Slack:ClientSecret"] ?? throw new InvalidOperationException("Slack:ClientSecret not configured"),
            ["redirect_uri"] = $"{baseUrl}/api/v1/oauth/callback"
        }), ct);

    var slackTokens = await slackResponse.Content.ReadFromJsonAsync<SlackOAuthResponse>(ct);
    if (!slackTokens!.Ok)
        throw new OAuthException($"Slack token exchange failed: {slackTokens.Error}");

    return new OAuthTokenResult(
        AccessToken: slackTokens.AccessToken,
        RefreshToken: null,
        ExpiresAt: null,      // Slack tokens don't expire
        TeamId: slackTokens.Team?.Id,
        BotUserId: slackTokens.BotUserId
    );
```

**GitHub OAuth token exchange:**
```csharp
case "github":
    var githubResponse = await httpClient.PostAsync(
        "https://github.com/login/oauth/access_token",
        new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = config["GitHub:ClientId"] ?? throw new InvalidOperationException("GitHub:ClientId not configured"),
            ["client_secret"] = config["GitHub:ClientSecret"] ?? throw new InvalidOperationException("GitHub:ClientSecret not configured")
        }), ct);

    var githubContent = await githubResponse.Content.ReadAsStringAsync(ct);
    // GitHub returns form-encoded response: access_token=...&scope=...&token_type=bearer
    var githubParams = HttpUtility.ParseQueryString(githubContent);
    var githubToken = githubParams["access_token"]
        ?? throw new OAuthException("GitHub token exchange failed — no access_token in response");

    return new OAuthTokenResult(AccessToken: githubToken, RefreshToken: null, ExpiresAt: null);
```

For Microsoft Teams/M365 (OAuth2 auth code with PKCE):
```csharp
case "microsoft":
    var msResponse = await httpClient.PostAsync(
        $"https://login.microsoftonline.com/{config["Microsoft:TenantId"] ?? "common"}/oauth2/v2.0/token",
        new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = config["Microsoft:ClientId"] ?? throw new InvalidOperationException("Microsoft:ClientId not configured"),
            ["client_secret"] = config["Microsoft:ClientSecret"] ?? throw new InvalidOperationException("Microsoft:ClientSecret not configured"),
            ["redirect_uri"] = $"{baseUrl}/api/v1/oauth/callback",
            ["grant_type"] = "authorization_code",
            ["scope"] = "https://graph.microsoft.com/.default offline_access"
        }), ct);

    var msTokens = await msResponse.Content.ReadFromJsonAsync<MicrosoftOAuthResponse>(ct);
    return new OAuthTokenResult(
        AccessToken: msTokens!.AccessToken,
        RefreshToken: msTokens.RefreshToken,
        ExpiresAt: DateTime.UtcNow.AddSeconds(msTokens.ExpiresIn)
    );
```

**Add to .env.example:**
```bash
# ─── OAuth App Credentials ──────────────────────────────────────────────────
# Slack — create at api.slack.com/apps
SLACK_CLIENT_ID=
SLACK_CLIENT_SECRET=

# GitHub — create at github.com/settings/developers
GITHUB_CLIENT_ID=
GITHUB_CLIENT_SECRET=

# Microsoft — create at portal.azure.com > App registrations
MICROSOFT_CLIENT_ID=
MICROSOFT_CLIENT_SECRET=
MICROSOFT_TENANT_ID=common
```

**OAuthService unit tests:**
- ExchangeCodeAsync_Slack_SendsCorrectFormParams (mock HttpClient)
- ExchangeCodeAsync_Slack_InvalidCode_ThrowsOAuthException
- ExchangeCodeAsync_GitHub_ParsesFormEncodedResponse
- ExchangeCodeAsync_Microsoft_ParsesJsonResponse_WithRefreshToken
- ExchangeCodeAsync_UnknownProvider_ThrowsNotSupportedException

**Remove mock_token stub:**
Delete the `return new OAuthTokenResult($"mock_token_{code}", ...)` fallback.
Unknown connectors should throw NotSupportedException with message listing supported providers.

### Fix 5 — E2E S34-S39 live execution

Run all Playwright scenarios S34-S39 against the live dev stack:
```bash
docker compose -f infrastructure/docker-compose.dev.yml up -d
cd backend && dotnet run --project Flowamaz.Api &
cd web && npm run dev &
cd web && npx playwright test e2e/connectors.spec.ts e2e/gates.spec.ts e2e/empathy.spec.ts --reporter=list
```

Fix any selector or timing failures. Confirm all 6 pass.

## Technical Requirements
- [ ] ConnectorCatalogueService: 8 tests, coverage ≥ 80%
- [ ] ConnectorSandbox: 5 tests, coverage ≥ 80%
- [ ] ByomProviderService: 3 tests, coverage ≥ 80%
- [ ] OAuthService: real token exchange for Slack, GitHub, Microsoft
- [ ] OAuthService: mock_token stub removed entirely
- [ ] OAuthService: 5 unit tests passing
- [ ] .env.example: OAuth client ID/secret vars documented
- [ ] E2E S34-S39: all 6 pass live
- [ ] dotnet build — 0 errors, 0 warnings
- [ ] dotnet test — all pass (will be 400+ total)

## Acceptance Criteria
- [ ] grep "mock_token" backend/ → 0 results
- [ ] ConnectorCatalogueService coverage ≥ 80% (Coverlet)
- [ ] Infrastructure layer coverage > 8.2% (improved by new tests)
- [ ] OAuthService: Slack exchange sends correct form params (unit test proves)
- [ ] S34-S39: 6/6 Playwright scenarios pass live

## Output Expected
```
backend/Flowamaz.Tests.Unit/Connectors/ConnectorCatalogueServiceTests.cs
backend/Flowamaz.Tests.Unit/Connectors/ConnectorSandboxTests.cs
backend/Flowamaz.Tests.Unit/Connectors/ByomProviderServiceTests.cs
backend/Flowamaz.Application/Connectors/Services/OAuthService.cs (real exchange)
backend/Flowamaz.Tests.Unit/Connectors/OAuthServiceTests.cs
.env.example (OAuth vars added)
results.md (E2E S34-S39 evidence)
```
