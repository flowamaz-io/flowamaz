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

---

## Prompt 04 — Authentication (JWT, refresh rotation, endpoints)  (2026-05-25)

**Status:** Complete · pushed to `develop`

**Built**
- Entity `RefreshToken` (hash-only, computed `IsRevoked`) + migration `AddRefreshTokens`.
- `IJwtService` + `JwtService` (HS256, 15-min access token with org + workspace claims; 256-bit refresh tokens, hash-only).
- `AuthService` (Application): register, login (lockout + no enumeration), refresh (rotation in a transaction), logout, /me.
- DTOs + FluentValidation validators (password ≥8, 1 upper, 1 number).
- `JwtAuthMiddleware` (Bearer JWT + `fmz_` API-key paths → `HttpContext.Items["CurrentUser"]`; fire-and-forget last-used in a detached scope).
- `HttpContextCurrentUserService` (in Api) implementing the extended `ICurrentUserService`.
- `AuthController`: register/login/refresh/logout/me with httpOnly `fmz_refresh` cookie (SameSite=Strict, Path=/api/v1/auth, Secure outside dev).
- Rate limiting: register 5/h, login 10/min (Redis fixed window). Exceptions: InvalidCredentials(401), AccountLocked(401), RateLimitExceeded(429). Global middleware maps FluentValidation → 400.
- Tests: JwtServiceTests, AuthServiceTests (unit), AuthEndpointTests (integration, Testcontainers Postgres+Redis). 76 unit + 13 integration green.

**DoD checklist**
- [x] dotnet build — 0 errors, 0 warnings
- [x] dotnet test — 76 unit + 13 integration pass
- [x] No user enumeration — wrong org / email / password all return identical 401 "Invalid credentials" (timing equalised with a dummy bcrypt verify); proven by integration test
- [x] Lockout — 5 failed attempts → 15-min lock; 6th rejected as locked (no remaining-time disclosed); proven by integration test
- [x] Refresh rotation — old token revoked atomically with new issue; replayed old cookie rejected; proven by integration test
- [x] Refresh token only in httpOnly cookie, never body; never logged
- [x] JWT workspace claims built from live DB memberships at login
- [x] Serilog entry/exit/error on all new functions
- [x] Scalar still mapped (no endpoint regressions)

**Deviations**
1. **`HttpContextCurrentUserService` lives in `Flowamaz.Api/Identity/`, not Infrastructure.** Reading `HttpContext` requires the web framework; keeping it in Api avoids adding a `FrameworkReference`/web dependency to Infrastructure (which broke the build via NU1510 by making prompt-01 packages redundant). `JwtService` (no HttpContext dependency) stays in Infrastructure as specified.
2. **`GenerateAccessToken` takes an extra `orgSlug` parameter** — `OrgUser` does not carry the org slug, which the `org_slug` claim needs.
3. **Responses use camelCase** (`accessToken`, `expiresIn`) per the existing ResponseWrapper/Web-JSON house style, not the snake_case shown illustratively in the prompt.
4. Added `Microsoft.Extensions.Options` to Application and `System.IdentityModel.Tokens.Jwt` 8.0.1 (matching the transitively-resolved version) to Infrastructure.
5. Replaced the prompt-01 `AnonymousCurrentUserService` stub with the real HttpContext-backed implementation (its own comment said this would happen in prompt 04).
