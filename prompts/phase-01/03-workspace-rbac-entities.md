---
prompt-id: 03-workspace-rbac-entities
phase: 01
sequence: 3
roles: [Executor, Verifier, Security]
type: feature
depends-on: [02-platform-org-entities]
estimated-complexity: High
---

# Workspace & RBAC Layer — Entities, Authorization Service, API Keys

## Context
Platform/org entities exist. Now build the workspace isolation layer — the primary
security boundary in Flowamaz. Every subsequent data access decision is governed by
workspace_id. Get this right — Verifier will flag any workspace isolation violation
as Critical. See FUNCTIONAL.md §4 for complete RBAC spec.

## Objective
Create workspace entities, 5-role RBAC model with numeric hierarchy, API key
management (hash-only storage), and WorkspaceAuthorizationService with full enforcement.

## Scope

### What to Build

**Entities (Flowamaz.Core/Entities/Workspace/):**

`Workspace`:
- Id (Guid), OrgId (FK → Organisation), Name, Slug (unique per org)
- Settings (owned/jsonb — WorkspaceSettings model)
- CreatedAt, UpdatedAt, IsDeleted, DeletedAt
- Index: (OrgId + Slug) unique

`WorkspaceMember`:
- Id (Guid), WorkspaceId (FK), OrgUserId (FK)
- Role (enum: Admin=5/Designer=4/Operator=3/Runner=2/Viewer=1)
- JoinedAt, IsActive (bool)
- Index: (WorkspaceId + OrgUserId) unique — one role per user per workspace

`WorkspaceEnvironment`:
- Id (Guid), WorkspaceId (FK)
- Name (enum: Dev/Staging/Production)
- CreatedAt
- Index: (WorkspaceId + Name) unique

`WorkspaceApiKey`:
- Id (Guid), WorkspaceId (FK), EnvironmentId (FK → WorkspaceEnvironment)
- Name (display only), KeyHash (SHA256, unique index — never store plain)
- KeyPrefix (first 12 chars of plain key — stored for display only, e.g. "fmz_live_fin")
- Scopes (List<string> jsonb)
- CreatedBy (Guid), LastUsedAt (DateTime?), ExpiresAt (DateTime?)
- IsActive (bool), CreatedAt, UpdatedAt

**WorkspaceSettings model (jsonb):**
MaxConcurrentRuns (int), RunRetentionDays (int), AiCostBudgetMonthUsd (decimal),
AllowedAiProviders (List<string>), DefaultAiModelOverrides (Dictionary<string,string>),
MarketplacePolicy (enum: AllowAll/OfficialAndVerified/Allowlist)

**API Key generation (WorkspaceApiKeyService):**
Format: `fmz_{env}_{workspace_slug}_{random32}`
- env: "live" for Production, "test" for Dev/Staging
- workspace_slug: first 8 chars of workspace slug, alphanumeric only
- random32: Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLower()
Example: `fmz_live_finops_a3f7b2c1d4e5f6a7b8c9d0e1f2a3b4c5`

Storage: SHA256 hash only. Plain value returned ONCE in CreateApiKeyResponse.PlainKey.
Never returned again. Never logged. Never in any response after creation.

KeyPrefix stored: first 16 chars of the generated key (safe — not enough to derive hash)

**Services:**

`IWorkspaceService` + `WorkspaceService`:
- CreateWorkspaceAsync(orgId, name, slug, createdByUserId) → Workspace
  — Validates slug uniqueness within org
  — Atomically creates: Workspace + 3 WorkspaceEnvironments (Dev/Staging/Production)
    + WorkspaceMember (creator as Admin)
  — All in one transaction
- GetByIdAsync(workspaceId, orgId) → Workspace? (org ownership check — returns null if mismatch)
- GetForOrgAsync(orgId) → List<Workspace>
- UpdateSettingsAsync(workspaceId, settings)
- SoftDeleteAsync(workspaceId)

`IWorkspaceMemberService` + `WorkspaceMemberService`:
- AddMemberAsync(workspaceId, orgUserId, role, addedByUserId) → WorkspaceMember
- UpdateRoleAsync(workspaceId, targetUserId, newRole, requestingUserId)
  — Prevents self-role-change (requestingUserId == targetUserId → throw)
- RemoveMemberAsync(workspaceId, targetUserId, requestingUserId)
  — Prevents self-removal (requestingUserId == targetUserId → throw)
  — Prevents removing last Admin
- GetMembersAsync(workspaceId) → List<WorkspaceMemberDto>
- GetMemberRoleAsync(workspaceId, orgUserId) → WorkspaceRole?
- IsMemberAsync(workspaceId, orgUserId) → bool

`IWorkspaceApiKeyService` + `WorkspaceApiKeyService`:
- CreateApiKeyAsync(workspaceId, environmentId, name, scopes, createdBy)
  → CreateApiKeyResponse { ApiKey (entity), PlainKey (string — returned ONCE, never again) }
- ValidateApiKeyAsync(plainKey) → WorkspaceApiKeyValidationResult?
  { WorkspaceId, EnvironmentId, Scopes, KeyId }
- RevokeApiKeyAsync(keyId, workspaceId)
- GetApiKeysAsync(workspaceId) → List<ApiKeyDto> (KeyPrefix visible, never plain)
- RecordLastUsedAsync(keyId)

`IWorkspaceAuthorizationService` + `WorkspaceAuthorizationService`:
- RequireMinimumRoleAsync(workspaceId, orgUserId, minimumRole) → throws InsufficientRoleException if actual < minimum
- HasPermissionAsync(workspaceId, orgUserId, permission) → bool
  Permission string examples: "workflows.publish.production", "credentials.manage", "members.manage"
  Maps each permission to minimum role from FUNCTIONAL.md §4.5
- IsWorkspaceAdminAsync(workspaceId, orgUserId) → bool
- ValidateApiKeyScopeAsync(scopes, requiredScope) → bool

**Permission → minimum role map (from FUNCTIONAL.md §4.5):**
```
workflows.*             → Designer (4)
workflows.publish.prod  → Admin (5)
instances.trigger       → Operator (3)
instances.variables.read→ Operator (3)
credentials.manage      → Admin (5)
members.manage          → Admin (5)
workspace.settings      → Admin (5)
audit.read              → Operator (3)
gates.decide            → Operator (3)
```

**Migration:** `AddWorkspaceSchema`

### What NOT to Build
- No workflow entities — Phase 2
- No credential vault — Phase 4
- No API endpoints — prompt 05
- No AI model config UI — Phase 3

## Technical Requirements
- [ ] CreateWorkspaceAsync: transaction creates workspace + 3 environments + admin member atomically
- [ ] Role enum numeric values enforced: Admin=5, Designer=4, Operator=3, Runner=2, Viewer=1
- [ ] RequireMinimumRoleAsync: throws InsufficientRoleException with role details in message
- [ ] API key plain value: in CreateApiKeyResponse.PlainKey only — never stored, never logged
- [ ] ValidateApiKeyAsync: SHA256 hash the incoming plain key, lookup by hash
- [ ] Self-role-change: throws SelfModificationException at service layer
- [ ] Self-removal: throws SelfModificationException at service layer
- [ ] Last-admin removal: throws LastAdminException at service layer
- [ ] GetByIdAsync: returns null if workspaceId belongs to different org (no 403 — just null, caller decides)
- [ ] Unit tests: full role hierarchy enforcement, API key create+validate+revoke, workspace isolation

## Acceptance Criteria
- [ ] dotnet build — zero errors
- [ ] dotnet test — all pass
- [ ] API key: SHA256 round-trip — generate key → hash → validate by hash works
- [ ] Role: Viewer (1) cannot pass Operator (3) minimum requirement
- [ ] Workspace isolation: GetByIdAsync with wrong orgId returns null
- [ ] Self-role-change: throws at service layer (not just API layer)
- [ ] CreateWorkspaceAsync: 3 environments always created, creator always Admin

## Output Expected
```
backend/Flowamaz.Core/Entities/Workspace/Workspace.cs
backend/Flowamaz.Core/Entities/Workspace/WorkspaceMember.cs
backend/Flowamaz.Core/Entities/Workspace/WorkspaceEnvironment.cs
backend/Flowamaz.Core/Entities/Workspace/WorkspaceApiKey.cs
backend/Flowamaz.Core/Enums/WorkspaceRole.cs
backend/Flowamaz.Core/Enums/WorkspaceEnvironmentType.cs
backend/Flowamaz.Core/Enums/ApiKeyScope.cs
backend/Flowamaz.Core/Enums/MarketplacePolicy.cs
backend/Flowamaz.Core/Models/WorkspaceSettings.cs
backend/Flowamaz.Core/Exceptions/InsufficientRoleException.cs
backend/Flowamaz.Core/Exceptions/SelfModificationException.cs
backend/Flowamaz.Core/Exceptions/LastAdminException.cs
backend/Flowamaz.Core/Interfaces/Services/IWorkspaceService.cs
backend/Flowamaz.Core/Interfaces/Services/IWorkspaceMemberService.cs
backend/Flowamaz.Core/Interfaces/Services/IWorkspaceApiKeyService.cs
backend/Flowamaz.Core/Interfaces/Services/IWorkspaceAuthorizationService.cs
backend/Flowamaz.Application/Workspace/Services/WorkspaceService.cs
backend/Flowamaz.Application/Workspace/Services/WorkspaceMemberService.cs
backend/Flowamaz.Application/Workspace/Services/WorkspaceApiKeyService.cs
backend/Flowamaz.Application/Workspace/Services/WorkspaceAuthorizationService.cs
backend/Flowamaz.Application/Workspace/DTOs/
backend/Flowamaz.Infrastructure/Persistence/Repositories/WorkspaceRepository.cs
backend/Flowamaz.Infrastructure/Persistence/Repositories/WorkspaceMemberRepository.cs
backend/Flowamaz.Infrastructure/Persistence/Repositories/WorkspaceApiKeyRepository.cs
backend/Flowamaz.Infrastructure/Persistence/Configurations/
backend/Flowamaz.Infrastructure/Persistence/Migrations/[ts]_AddWorkspaceSchema.cs
backend/Flowamaz.Tests.Unit/Workspace/WorkspaceServiceTests.cs
backend/Flowamaz.Tests.Unit/Workspace/WorkspaceMemberServiceTests.cs
backend/Flowamaz.Tests.Unit/Workspace/WorkspaceApiKeyServiceTests.cs
backend/Flowamaz.Tests.Unit/Workspace/WorkspaceAuthorizationServiceTests.cs
```

## Notes for Executor
- API key generation: use System.Security.Cryptography.RandomNumberGenerator — not Random
- SHA256 for API key: System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(plainKey))
  → Convert.ToHexString(hash).ToLower()
- WorkspaceRole enum: assign integer values explicitly so numeric comparison works:
  Admin = 5, Designer = 4, Operator = 3, Runner = 2, Viewer = 1
- The permission map is a Dictionary<string, WorkspaceRole> in WorkspaceAuthorizationService —
  not a database table. Seeded in the service constructor.
- CreateApiKeyResponse.PlainKey must be [JsonIgnore] on the entity class itself — only
  on the response DTO is it exposed (once)
