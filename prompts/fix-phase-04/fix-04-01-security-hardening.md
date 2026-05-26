---
prompt-id: fix-04-01-security-hardening
phase: fix-phase-04
sequence: 1
roles: [Executor, Verifier, Security]
type: fix
source-issue: Verifier/Security — Phase 04 report
severity: Critical
depends-on: []
---

# Fix: GATE_SIGNING_KEY Default + SLACK_SIGNING_SECRET Bypass + DB-Level Workspace Isolation

## Issues Being Fixed

```
C1 — GATE_SIGNING_KEY defaults to "default-dev-key"
FILE: GateApprovalController.cs line 186
SEVERITY: Critical
FINDING: Attacker knowing this string can forge HMAC signatures and approve
  any human gate across all workspaces.
FIX REQUIRED: Throw at startup if GATE_SIGNING_KEY not set in non-Development.
```

```
M1 — SLACK_SIGNING_SECRET bypass
FILE: GateApprovalController.cs lines 227-231
SEVERITY: Major
FINDING: Missing secret causes ValidateSlackSignatureAsync to return true —
  any caller can approve/reject any gate without a valid Slack signature.
FIX REQUIRED: Treat missing SLACK_SIGNING_SECRET as hard failure in non-Development.
```

```
Minor — CredentialVaultService workspace isolation is post-fetch
FILE: CredentialVaultService.cs lines 122, 175
FINDING: RetrieveAsync/RevokeAsync fetch by credentialId only, then check workspaceId
  in-process. DB fetches unauthorised rows before throwing.
FIX REQUIRED: Add workspaceId to EF query predicates.
```

```
Minor — GATE_SIGNING_KEY and SLACK_SIGNING_SECRET missing from .env.example
FIX REQUIRED: Add both with placeholder values and security comments.
```

## Objective
Harden gate security so the app is safe to run and test. All 4 fixes in this prompt.

## Scope

### Fix 1 — GATE_SIGNING_KEY startup enforcement

In `StartupMigrationService` or Program.cs startup validation block, add:
```csharp
if (!app.Environment.IsDevelopment())
{
    var gateKey = app.Configuration["GATE_SIGNING_KEY"];
    if (string.IsNullOrEmpty(gateKey) || gateKey == "default-dev-key")
        throw new InvalidOperationException(
            "GATE_SIGNING_KEY must be set in non-Development environments. " +
            "Generate with: openssl rand -hex 32");

    var slackSecret = app.Configuration["SLACK_SIGNING_SECRET"];
    if (string.IsNullOrEmpty(slackSecret))
        throw new InvalidOperationException(
            "SLACK_SIGNING_SECRET must be set when Slack integration is enabled. " +
            "Find this in your Slack app settings under 'Basic Information'.");
}
```

In `GateApprovalController`:
- Remove `?? "default-dev-key"` fallback entirely
- If GATE_SIGNING_KEY is null at the point of use → throw InvalidOperationException
  (startup validation already ensures this never happens in production)
- In Development: log a Warning "Using Development GATE_SIGNING_KEY — not suitable for production"
  and use a random ephemeral key (not a deterministic string)

In `SlackActionsController` (or wherever ValidateSlackSignatureAsync is called):
- If SLACK_SIGNING_SECRET is null or empty:
  - In Development: log Warning "SLACK_SIGNING_SECRET not set — skipping Slack signature validation in Development"
    and return true (allow in dev only)
  - In Production/Staging: return 503 Service Unavailable with body:
    "Slack integration not configured. Set SLACK_SIGNING_SECRET."

### Fix 2 — CredentialVaultService DB-level workspace isolation

In `CredentialVaultService.RetrieveAsync`:
```csharp
// BEFORE (post-fetch check):
var credential = await _dbContext.WorkspaceCredentials
    .FirstOrDefaultAsync(c => c.Id == credentialId, ct);
if (credential?.WorkspaceId != workspaceId)
    throw new UnauthorisedException(...);

// AFTER (DB-level filter):
var credential = await _dbContext.WorkspaceCredentials
    .FirstOrDefaultAsync(c => c.Id == credentialId
                            && c.WorkspaceId == workspaceId, ct);
if (credential == null)
    throw new UnauthorisedException("Credential not found or access denied.");
```

Apply same fix to `RevokeAsync` and any other method that looks up by credentialId alone.

### Fix 3 — .env.example additions

Add to .env.example:
```bash
# ─── Gate Security ─────────────────────────────────────────────────────────
# REQUIRED in non-Development. Generate with: openssl rand -hex 32
# Used to sign email gate approval links. Forgeable if weak or missing.
GATE_SIGNING_KEY=

# REQUIRED if using Slack connector for human gate approvals.
# Found in Slack app settings > Basic Information > Signing Secret.
# Set to non-empty value to validate Slack webhook signatures.
SLACK_SIGNING_SECRET=

# REQUIRED if using Microsoft Teams connector for human gate approvals.
# Your Teams bot/app client secret from Azure AD app registration.
TEAMS_BOT_SECRET=
```

### Fix 4 — Add GATE_SIGNING_KEY to FUNCTIONAL.md env vars section

In FUNCTIONAL.md environment variables section, add:
```
GATE_SIGNING_KEY=        # 32-byte hex — required non-Development
SLACK_SIGNING_SECRET=    # from Slack app settings
TEAMS_BOT_SECRET=        # from Azure AD app registration
```

### Unit tests to add:

GateApprovalControllerTests (extend):
- ApproveEmailLink_ValidHmac_Returns302Redirect
- ApproveEmailLink_InvalidHmac_Returns400
- ApproveEmailLink_ExpiredHmac_Returns400 (timestamp beyond 72h)
- ApproveEmailLink_WrongWorkspace_Returns400

SlackActionsControllerTests:
- SlackAction_ValidSignature_ProcessesGate
- SlackAction_InvalidSignature_Returns401
- SlackAction_MissingSecretInDevelopment_ReturnsTrue (dev-only bypass)

CredentialVaultServiceTests (extend):
- RetrieveAsync_WrongWorkspaceId_ThrowsUnauthorised
- RevokeAsync_WrongWorkspaceId_ThrowsUnauthorised

## Technical Requirements
- [ ] GATE_SIGNING_KEY null/default in Production → startup exception, app does not start
- [ ] SLACK_SIGNING_SECRET null in Production → 503 on Slack actions endpoint
- [ ] Development: both missing → Warning log, app starts (for local dev convenience)
- [ ] CredentialVaultService: workspaceId in EF WHERE clause, not post-fetch check
- [ ] .env.example: GATE_SIGNING_KEY, SLACK_SIGNING_SECRET, TEAMS_BOT_SECRET documented
- [ ] dotnet build — 0 errors, 0 warnings
- [ ] dotnet test — all pass

## Acceptance Criteria
- [ ] Integration test: startup without GATE_SIGNING_KEY in Staging env → throws
- [ ] Unit test: invalid HMAC on email link → 400
- [ ] Unit test: missing SLACK_SIGNING_SECRET in Production → 503
- [ ] Unit test: RetrieveAsync with wrong workspaceId → UnauthorisedException
- [ ] grep "default-dev-key" across entire codebase → 0 results
- [ ] .env.example contains GATE_SIGNING_KEY with comment

## Output Expected
```
backend/Flowamaz.Api/Program.cs (startup validation added)
backend/Flowamaz.Api/Controllers/GateApprovalController.cs (default removed)
backend/Flowamaz.Api/Controllers/SlackActionsController.cs (missing secret hardened)
backend/Flowamaz.Infrastructure/Services/CredentialVaultService.cs (DB-level filter)
backend/Flowamaz.Tests.Unit/Gates/GateApprovalControllerTests.cs (extended)
backend/Flowamaz.Tests.Unit/Gates/SlackActionsControllerTests.cs (extended)
backend/Flowamaz.Tests.Unit/Connectors/CredentialVaultServiceTests.cs (extended)
.env.example (3 new vars added)
FUNCTIONAL.md (env vars section updated)
```

## Notes for Executor
- Development ephemeral key: use RandomNumberGenerator.GetBytes(32) stored as
  a static field initialised once on startup — random per process start, not deterministic
- The startup validation block should run AFTER the DI container is built but
  BEFORE app.Run() — same pattern as StartupMigrationService
- grep test: `grep -r "default-dev-key" backend/` must return 0 results
