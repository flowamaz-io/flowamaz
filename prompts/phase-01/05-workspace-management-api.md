---
prompt-id: 05-workspace-management-api
phase: 01
sequence: 5
roles: [Executor, Verifier, Security]
type: feature
depends-on: [04-authentication-endpoints]
estimated-complexity: Medium
---

# Workspace & Member Management API

## Context
Auth works. All services exist. Now expose workspace CRUD, member management,
and API key management as REST endpoints. All workspace endpoints enforce RBAC
via custom authorization attributes. See FUNCTIONAL.md §4 for permissions.

## Objective
WorkspacesController, WorkspaceMembersController, WorkspaceApiKeysController with
RequireWorkspaceRole attribute, full integration tests, and AI model settings UI
data endpoints.

## Scope

### What to Build

**WorkspacesController (/api/v1/workspaces):**
- GET / → [Authorize] — all workspaces where requesting user is an active member
- POST / → [Authorize] — create workspace (OrgOwner check via CurrentUserService)
- GET /{id} → [RequireWorkspaceRole(Viewer)] — workspace detail + settings
- PUT /{id}/settings → [RequireWorkspaceRole(Admin)] — update WorkspaceSettings
  — Validates AllowedAiProviders against org's providers_enabled
- DELETE /{id} → [Authorize] — soft delete (OrgOwner check)

**WorkspaceMembersController (/api/v1/workspaces/{workspaceId}/members):**
- GET / → [RequireWorkspaceRole(Admin)] — list members with roles
- POST / → [RequireWorkspaceRole(Admin)] — add member by email
  — Lookup OrgUser by email+orgId
  — If not found: 404 { message: "No user with that email exists in your organisation. They must register first." }
  — If already member: 409 "Already a member"
- PATCH /{userId}/role → [RequireWorkspaceRole(Admin)] — update role
  — Prevents self role-change: 400 "You cannot change your own role"
  — Prevents removing last Admin: 400 "Cannot remove the last workspace admin"
- DELETE /{userId} → [RequireWorkspaceRole(Admin)] — remove member
  — Prevents self-removal: 400 "You cannot remove yourself"
  — Prevents removing last Admin: 400

**WorkspaceApiKeysController (/api/v1/workspaces/{workspaceId}/api-keys):**
- GET / → [RequireWorkspaceRole(Admin)] — list (KeyPrefix + name + scopes + lastUsedAt)
  — PlainKey is NEVER in list response — null always
- POST / → [RequireWorkspaceRole(Admin)] — create key
  — Response includes PlainKey ONCE — clearly labelled "Store this now — it will not be shown again"
  — Response header: X-Plain-Key-One-Time: true
- DELETE /{keyId} → [RequireWorkspaceRole(Admin)] — revoke

**AI Model Config endpoint:**
- GET /api/v1/workspaces/{id}/ai-config → [RequireWorkspaceRole(Admin)]
  Returns: resolved model config per function (showing which level it resolves from:
  workspace/org/platform), allowed providers, workspace budget status
- PUT /api/v1/workspaces/{id}/ai-config → [RequireWorkspaceRole(Admin)]
  Updates workspace_ai_config function_overrides. Validates provider against org allowlist.

**RequireWorkspaceRoleAttribute:**
- IAsyncAuthorizationFilter implementation
- Reads workspaceId from route (key: "workspaceId" or "id" context-aware)
- Calls IWorkspaceAuthorizationService.RequireMinimumRoleAsync
- On API key request: validates workspace matches key's workspace, checks required scope
- Returns 403 with message: "Insufficient permissions. Required role: {role}."

**Org ownership check:**
- RequireOrgOwnerAttribute — checks CurrentUserService.IsOrgOwner via JWT claim
  (add is_org_owner boolean claim to JWT in prompt 04's JwtService.GenerateAccessToken)

**Pagination:**
All list endpoints support: ?page=1&pageSize=20 (default pageSize 20, max 100)
Response wrapper includes: { data: [], pagination: { page, pageSize, total, totalPages } }

### What NOT to Build
- No workflow endpoints — Phase 2+
- No credential vault — Phase 4
- No email invitations (look up existing OrgUser only)

## Technical Requirements
- [ ] Workspace isolation: every controller method verifies org ownership of workspace
- [ ] API key PlainKey: never appears in GET /api-keys — explicitly set to null in mapping
- [ ] API key scope check: API key requests validate required scope → 403 with scope name
- [ ] AI config: cannot set provider not in org's providers_enabled → 400 with message
- [ ] AI config: cannot set model without required capability (F3 without vision) → 400
- [ ] Pagination on all list endpoints
- [ ] Integration tests: full workspace + member CRUD lifecycle
- [ ] Integration tests: API key create → validate by scope → revoke → validate fails

## Acceptance Criteria
- [ ] POST /workspaces/members with unknown email → 404 with helpful message
- [ ] GET /workspaces/{id}/api-keys → PlainKey is null in every item
- [ ] POST /workspaces/{id}/api-keys → PlainKey present in response, X-Plain-Key-One-Time header set
- [ ] Viewer role → POST /members → 403 "Insufficient permissions"
- [ ] Wrong org's workspace → 404 (not 403 — do not reveal workspace exists)
- [ ] AI config: set Azure model when org only allows Anthropic → 400

## Output Expected
```
backend/Flowamaz.Api/Controllers/WorkspacesController.cs
backend/Flowamaz.Api/Controllers/WorkspaceMembersController.cs
backend/Flowamaz.Api/Controllers/WorkspaceApiKeysController.cs
backend/Flowamaz.Api/Authorization/RequireWorkspaceRoleAttribute.cs
backend/Flowamaz.Api/Authorization/RequireOrgOwnerAttribute.cs
backend/Flowamaz.Api/Authorization/WorkspaceAuthorizationHandler.cs
backend/Flowamaz.Application/Workspace/DTOs/ (all request/response DTOs)
backend/Flowamaz.Application/Workspace/Validators/
backend/Flowamaz.Tests.Integration/Workspace/WorkspaceApiTests.cs
backend/Flowamaz.Tests.Integration/Workspace/MemberApiTests.cs
backend/Flowamaz.Tests.Integration/Workspace/ApiKeyTests.cs
```

## Notes for Executor
- "Wrong org's workspace" must return 404 not 403 — returning 403 reveals the workspace exists
- RequireWorkspaceRoleAttribute: read route values carefully — some routes use "id",
  some use "workspaceId". Handle both.
- AI config validation calls IModelResolutionService.ValidateConfigAsync before saving —
  reuse the same validation logic from prompt 01
- Add is_org_owner claim to JWT (update JwtService from prompt 04):
  claims.Add(new Claim("is_org_owner", user.IsOrgOwner.ToString().ToLower()))
