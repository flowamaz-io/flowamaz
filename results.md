# Flowamaz — Execution Results

PM Agent appends after every prompt completes. Never overwrite — only append.

---

## Prompt 03 — Workspace & RBAC Entities  (2026-05-25)

**Status:** Complete · pushed to `develop`

**Built**
- Enums: `WorkspaceRole` (Admin=5…Viewer=1), `WorkspaceEnvironmentType`, `MarketplacePolicy`; `ApiKeyScope` constants (FUNCTIONAL §4.6).
- Model: `WorkspaceSettings` (jsonb).
- Entities: `Workspace`, `WorkspaceMember`, `WorkspaceEnvironment`, `WorkspaceApiKey` (all `BaseEntity`, soft-delete filtered).
- Exceptions: `InsufficientRoleException` (403), `SelfModificationException` (409), `LastAdminException` (409).
- Services: `WorkspaceService`, `WorkspaceMemberService`, `WorkspaceApiKeyService`, `WorkspaceAuthorizationService` (Serilog entry/exit/error on every method).
- Repositories + EF configs for all 4 entities; DbSets + DI wired.
- Migration `20260525054353_AddWorkspaceSchema` — applied to dev DB and verified on fresh Postgres (Testcontainers).
- Unit tests: 32 new (61 total).

**DoD checklist**
- [x] All declared output files exist (see deviations for paths)
- [x] dotnet build — 0 errors, 0 warnings
- [x] dotnet test — 61 unit + 7 integration pass
- [x] Serilog entry/exit/error on all new service methods
- [x] Workspace isolation: every member/api-key/env read filtered by workspace_id; `GetByIdAsync` org-scoped returns null on mismatch
- [x] Credential security: API keys stored as SHA256 hash only; plain key returned once via `CreateApiKeyResponse.PlainKey`, never logged
- [x] Role hierarchy enforced numerically; Viewer(1) fails Operator(3) requirement
- [x] Self-role-change / self-removal / last-admin all throw at the service layer
- [x] API key SHA256 round-trip verified by test
- [N/A] Scalar 200 / Vue / empty-error states — no endpoints or UI in this prompt (prompt 05 / 06)

**Deviations**
1. **DTO/result types in `Core/Models`, not `Application/Workspace/DTOs/`.** The service interfaces live in `Core` and clean architecture forbids `Core → Application` references, so `WorkspaceMemberDto`, `ApiKeyDto`, `CreateApiKeyResponse`, `WorkspaceApiKeyValidationResult` are returned from `Core/Models` (mirrors the existing `OrgRegistrationResult` precedent). Clean architecture is a Critical rule and wins over the suggested file path.
2. **Entity namespace `Flowamaz.Core.Entities.Workspaces` (plural); test namespace `Flowamaz.Tests.Unit.Workspaces`.** Files still live under the `Entities/Workspace/` and `Tests.Unit/Workspace/` folders per spec. A singular `…Workspace` namespace collides with the `Workspace` type name (CS0118), so the namespace is pluralised.
3. **`KeyPrefix` = first 16 chars** (per "Notes for Executor"), not the 12 shown in the inline entity example.
4. **`ValidateApiKeyScope` is synchronous** (no I/O) rather than `…Async` — it does not await anything.
5. Added repository interfaces (`IWorkspaceRepository`, `IWorkspaceMemberRepository`, `IWorkspaceApiKeyRepository`) in `Core` — required by clean architecture; not separately enumerated in the prompt's output list.
