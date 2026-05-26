---
prompt-id: 04-02-official-connectors
phase: 04
sequence: 2
roles: [Executor, Verifier, Security]
type: feature
depends-on: [04-01-connector-sdk-spec]
estimated-complexity: High
---

# Official Connectors + Guided OAuth Wizard

## Context
Connector SDK, credential vault, and sandbox exist. Now implement all 13 official
connectors and the guided OAuth wizard that eliminates n8n's #1 UX complaint.
See FUNCTIONAL.md §9.1 (official connectors) and §9.4 (guided OAuth).

## Objective
Implement all 13 official connectors as connector.yaml manifests + operation handlers.
Build the guided OAuth wizard that completes connector auth in one click.
Build the connector health dashboard.

## Scope

### What to Build

**13 Official Connectors (connector manifests + .NET operation implementations):**

For each connector: `connector.yaml` manifest + `ConnectorHandler` class.

1. **HTTP/REST** — generic HTTP connector
   Operations: GET, POST, PUT, PATCH, DELETE, HEAD
   Auth: api-key, bearer, basic, none
   No allowed_domains restriction (user declares target URL)

2. **PostgreSQL** — direct SQL
   Operations: query, execute, transaction
   Auth: connection-string
   allowed_domains: user's DB host (declared at credential time)

3. **MySQL** — direct SQL
   Operations: query, execute
   Auth: connection-string

4. **Slack** — messaging + approvals
   Operations: send-message, send-dm, create-channel, upload-file,
     post-approval-message (sends interactive button message for human gate)
   Auth: oauth2-auth-code
   allowed_domains: [slack.com, hooks.slack.com]

5. **Microsoft Teams** — messaging + adaptive cards
   Operations: send-message, send-dm, post-adaptive-card
     (Adaptive Card with Approve/Reject buttons for human gate)
   Auth: oauth2-auth-code (Microsoft identity platform)
   allowed_domains: [graph.microsoft.com, login.microsoftonline.com]

6. **Email (SMTP/Resend)** — transactional email
   Operations: send-email, send-template-email
   Auth: api-key (Resend) or SMTP credentials
   allowed_domains: [api.resend.com] or user SMTP host

7. **Webhook Emit** — outbound webhooks
   Operations: send-webhook (POST to URL with HMAC signature)
   Auth: hmac-secret
   No domain restriction

8. **Webhook Receive** — inbound trigger
   Operations: creates a webhook endpoint at hooks.flowamaz.io/{workspaceId}/{workflowId}
   Auth: hmac-validate
   Trigger type: webhook

9. **File System** — local file operations (community edition)
   Operations: read-file, write-file, list-directory, delete-file
   Auth: none (restricted to configured base path)

10. **Schedule/Cron** — time-based triggers
    Operations: cron expression trigger
    Auth: none
    Trigger type: schedule

11. **Script/Shell** — execute shell scripts
    Operations: execute-script (bash/python/node)
    Auth: none
    WARNING: restricted to Designer+ role, disabled by default

12. **GitHub** — DevOps
    Operations: create-issue, get-issue, create-pr, add-comment, trigger-workflow
    Auth: oauth2-auth-code or PAT
    allowed_domains: [api.github.com, github.com]

13. **Microsoft 365** — productivity
    Operations: send-email, get-calendar-events, create-calendar-event,
      get-sharepoint-file, upload-sharepoint-file
    Auth: oauth2-auth-code (Microsoft Graph API)
    allowed_domains: [graph.microsoft.com, login.microsoftonline.com]

**Operation Handler Architecture:**

Each connector has a corresponding .NET class implementing `IConnectorOperationHandler`:
```csharp
interface IConnectorOperationHandler {
    string ConnectorId { get; }
    string OperationId { get; }
    Task<JsonDocument> ExecuteAsync(
        JsonDocument input,
        WorkspaceCredential credential,
        CancellationToken ct);
}
```

HTTP-based connectors (Slack, Teams, GitHub, M365, Resend): use HttpClientFactory.
Database connectors (PostgreSQL, MySQL): use Npgsql/MySqlConnector.
File/Shell connectors: restricted execution with path validation.

**Guided OAuth Wizard:**

`POST /api/v1/workspaces/{workspaceId}/connectors/{connectorId}/oauth/initiate`
- Generates OAuth state parameter (store in Redis 10-min TTL)
- Returns: { authorization_url, state }
- Frontend: window.open(authorization_url) — OAuth flow in popup

`GET /api/v1/oauth/callback?code={code}&state={state}`
- Validates state from Redis
- Exchanges code for tokens
- Stores tokens in CredentialVaultService
- Closes popup, sends PostMessage to parent window: { success: true, credentialId }
- Frontend receives PostMessage → connector marked as configured

This is the "one click" OAuth flow that replaces manual Client ID / redirect URL config.

**Connector Health Dashboard:**

`GET /api/v1/workspaces/{workspaceId}/connectors/health`
Returns per-credential health:
```json
[{
  "connector_id": "slack",
  "credential_name": "Flowamaz Slack workspace",
  "status": "healthy | expired | rate_limited | error",
  "last_used_at": "...",
  "error_message": null,
  "expires_at": "...",
  "calls_last_hour": 12
}]
```

Status detection:
- expired: credential.ExpiresAt < UtcNow
- rate_limited: Redis counter for this credential exceeds connector's rate_limit
- error: last operation returned 4xx/5xx within last 5 minutes
- healthy: none of the above

**Unit tests:**
- SlackConnectorHandler: send-message → correct HTTP call (mock HttpClient)
- OAuthCallbackHandler: valid state → tokens stored in vault
- OAuthCallbackHandler: invalid/expired state → 400
- ConnectorHealthService: expired credential → status=expired
- PostgreSqlConnectorHandler: SQL injection attempt in query → rejected

## Technical Requirements
- [ ] All 13 connector manifests valid against connector-manifest.schema.json
- [ ] No wildcard allowed_domains in any official connector
- [ ] OAuth state stored in Redis with 10-min TTL
- [ ] OAuth tokens stored encrypted in CredentialVaultService
- [ ] Script/Shell connector disabled by default (requires explicit opt-in in WorkspaceSettings)
- [ ] Connector operation input validated against input_schema before execution
- [ ] HttpClientFactory used for all HTTP connectors (no new HttpClient())

## Acceptance Criteria
- [ ] POST /oauth/initiate → returns valid authorization_url for Slack
- [ ] OAuth callback → credential stored, popup closes with PostMessage
- [ ] GET /connectors/health → correct status for expired credential
- [ ] SlackConnectorHandler: mock test proves correct HTTP call shape
- [ ] All 13 connector manifests exist and pass schema validation

## Output Expected
```
backend/Flowamaz.Application/Connectors/Handlers/ (13 handler classes)
backend/Flowamaz.Application/Connectors/Services/OAuthService.cs
backend/Flowamaz.Application/Connectors/Services/ConnectorHealthService.cs
backend/Flowamaz.Api/Controllers/ConnectorsController.cs
backend/Flowamaz.Api/Controllers/OAuthCallbackController.cs
backend/Flowamaz.Tests.Unit/Connectors/ (handler tests)
backend/Flowamaz.Tests.Integration/Connectors/OAuthFlowTests.cs
```
