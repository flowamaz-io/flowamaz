---
prompt-id: 04-authentication-endpoints
phase: 01
sequence: 4
roles: [Executor, Verifier, Security]
type: feature
depends-on: [03-workspace-rbac-entities]
estimated-complexity: High
---

# Authentication — JWT, Refresh Token Rotation, All Auth Endpoints

## Context
All entities and services exist. Build the complete authentication system including
JWT generation, refresh token rotation, lockout enforcement, and all auth endpoints.
Security rules are non-negotiable — see FUNCTIONAL.md §12.1.

## Objective
Full JWT auth: register, login (with lockout), refresh (with rotation), logout, me
endpoints, JwtAuthMiddleware, API key auth path, and CurrentUserService.

## Scope

### What to Build

**Entity:**
`RefreshToken` (Flowamaz.Core/Entities/Auth/):
- Id (Guid), OrgUserId (FK → OrgUser), TokenHash (SHA256, unique index)
- ExpiresAt (DateTime), RevokedAt (DateTime?), CreatedAt, CreatedByIp (string)
- IsRevoked → computed: RevokedAt.HasValue || ExpiresAt < UtcNow
Migration: `AddRefreshTokens`

**JwtService (Flowamaz.Infrastructure/Services/):**
- GenerateAccessToken(OrgUser, List<WorkspaceMembership>) → string
  Claims: sub (orgUserId), email, org_id, org_slug, name,
  workspaces (array: [{workspace_id, workspace_slug, role_name, role_value}])
  Expiry: 15 minutes. Algorithm: HS256. Key: from config, min 32 chars.
- GenerateRefreshToken() → (PlainToken: string, Hash: string)
  PlainToken: Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLower()
  Hash: SHA256(plainToken) — stored, never the plain token
  Expiry: 7 days from UtcNow
- ValidateAccessToken(token) → ClaimsPrincipal? (null if invalid/expired)
- ExtractOrgUserId(ClaimsPrincipal) → Guid
- ExtractOrgId(ClaimsPrincipal) → Guid
- ExtractWorkspaceMemberships(ClaimsPrincipal) → List<WorkspaceMembership>

**Auth endpoints (Flowamaz.Api/Controllers/AuthController.cs):**

POST /api/v1/auth/register
- Rate limit: 5/hour/IP (Redis counter with 1h TTL keyed on "register:{ip}")
- Validates: org name, slug, email, password (min 8 chars, 1 upper, 1 number), plan
- Calls OrganisationService.RegisterOrganisationAsync
- Returns: { access_token, expires_in, user: { id, email, name, org_id, org_slug } }
- Also sets refreshToken httpOnly cookie (7 day, SameSite=Strict, Secure)
- On duplicate slug: 409 with message "This organisation URL is already taken"
- On other error: 400 with validation details

POST /api/v1/auth/login
- Rate limit: 10/min/IP (Redis counter)
- Body: { email, password, org_slug }
- Lookup org by slug → lookup user by email+orgId
- No user enumeration: wrong org_slug, wrong email, wrong password →
  all return 401 { message: "Invalid credentials" } — same message, same timing
- Lockout check: if IsLockedOutAsync → 401 { message: "Account temporarily locked. Try again later." }
  (do NOT say how long)
- On wrong password: RecordFailedLoginAsync → if FailedLoginCount >= 5: LockAccountAsync(15 min)
- On success: ResetFailedLoginCountAsync + UpdateLastLoginAsync
- Returns: { access_token, expires_in, user } + refreshToken cookie

POST /api/v1/auth/refresh
- Reads refreshToken from httpOnly cookie (not body — never accept in body)
- SHA256 hash the cookie value → lookup RefreshToken by hash
- Validates: not revoked, not expired, user is active
- Rotation: mark old token as revoked (RevokedAt = UtcNow) → generate new pair
- Returns: new { access_token, expires_in } + new refreshToken cookie (old cookie replaced)
- On invalid/expired: 401, clear the cookie

POST /api/v1/auth/logout
- Reads refreshToken cookie → hash → revoke in DB
- Clears the refreshToken cookie (set expired)
- Returns: 200

GET /api/v1/auth/me
- [Authorize] — requires valid access token
- Returns: { id, email, name, org_id, org_slug, workspaces: [{id, slug, name, role}] }

**JwtAuthMiddleware (Flowamaz.Api/Middleware/):**
On every request except /api/v1/auth/*, /scalar, /health:
1. Check Authorization header for "Bearer {token}"
   → If present: validate JWT → attach CurrentUserContext to HttpContext.Items
2. Check fmz_ prefix on token (API key path)
   → If present: ValidateApiKeyAsync → build workspace-scoped context
3. If neither: request proceeds unauthenticated (controllers decide whether to require auth)

API key context: IsHumanUser=false, IsApiKey=true, WorkspaceId set from key,
EnvironmentId set from key, Scopes from key.

**CurrentUserService (Flowamaz.Infrastructure/Services/):**
- Reads from HttpContext.Items["CurrentUser"]
- Exposes: OrgUserId (Guid?), OrgId (Guid?), OrgSlug (string?)
- WorkspaceMemberships (List<WorkspaceMembership>)
- IsAuthenticated, IsHumanUser, IsApiKey
- GetWorkspaceRole(workspaceId) → WorkspaceRole? (from memberships in JWT)

**RefreshToken cookie settings:**
- Name: "fmz_refresh"
- HttpOnly: true (JavaScript cannot read it)
- Secure: true (HTTPS only — check env, allow HTTP in Development)
- SameSite: Strict
- Path: /api/v1/auth (only sent to auth endpoints)
- MaxAge: 7 days

### What NOT to Build
- No SSO/SAML — Phase 6
- No password reset — add in Phase 2 fix phase
- No email verification — Phase 2
- No 2FA — Phase 6

## Technical Requirements
- [ ] No user enumeration: wrong org, wrong email, wrong password → identical 401 response
- [ ] Lockout: 5 failed attempts → 15 min lock. Stored in OrgUser entity (not Redis)
- [ ] Refresh token rotation: old token revoked atomically with new token creation (transaction)
- [ ] Refresh token in httpOnly cookie — never in response body, never accepted from body
- [ ] JWT workspace claims built from actual DB memberships at login — not inferred
- [ ] API key RecordLastUsedAsync: fire-and-forget (does not block request)
- [ ] Rate limiting: separate Redis keys for register and login
- [ ] Integration test: full register → login → refresh → logout → refresh fails flow
- [ ] Security test: verify 5 failed logins triggers lockout
- [ ] Security test: verify old refresh token rejected after rotation

## Acceptance Criteria
- [ ] Register → login → /me → logout flow works end to end
- [ ] Wrong password: same 401 as wrong email (verified by comparing response bodies)
- [ ] Lockout: 5th failed login triggers 15-min lock, 6th login returns locked message
- [ ] Refresh rotation: old token cannot be reused after refresh
- [ ] /api/v1/auth/me with no token → 401
- [ ] /api/v1/auth/me with valid token → 200 with workspace memberships
- [ ] API key auth: fmz_live_ prefix validated, workspace context set correctly
- [ ] Cookie: HttpOnly, Secure (in non-dev), SameSite=Strict, Path=/api/v1/auth

## Output Expected
```
backend/Flowamaz.Core/Entities/Auth/RefreshToken.cs
backend/Flowamaz.Core/Interfaces/Services/IJwtService.cs
backend/Flowamaz.Application/Auth/Services/AuthService.cs
backend/Flowamaz.Application/Auth/DTOs/LoginRequest.cs
backend/Flowamaz.Application/Auth/DTOs/LoginResponse.cs
backend/Flowamaz.Application/Auth/DTOs/RegisterRequest.cs
backend/Flowamaz.Application/Auth/DTOs/RegisterResponse.cs
backend/Flowamaz.Application/Auth/Validators/LoginRequestValidator.cs
backend/Flowamaz.Application/Auth/Validators/RegisterRequestValidator.cs
backend/Flowamaz.Infrastructure/Services/JwtService.cs
backend/Flowamaz.Infrastructure/Services/CurrentUserService.cs
backend/Flowamaz.Infrastructure/Persistence/Repositories/RefreshTokenRepository.cs
backend/Flowamaz.Infrastructure/Persistence/Migrations/[ts]_AddRefreshTokens.cs
backend/Flowamaz.Api/Middleware/JwtAuthMiddleware.cs
backend/Flowamaz.Api/Controllers/AuthController.cs
backend/Flowamaz.Tests.Unit/Auth/JwtServiceTests.cs
backend/Flowamaz.Tests.Unit/Auth/AuthServiceTests.cs
backend/Flowamaz.Tests.Integration/Auth/AuthEndpointTests.cs
```

## Notes for Executor
- Lockout timing: do NOT disclose remaining lockout time in error messages
  ("Account temporarily locked" — not "locked for 14 more minutes")
- Rate limit keys: "ratelimit:register:{ip}" and "ratelimit:login:{ip}"
  Use Redis INCR + EXPIRE pattern (not sliding window for these)
- RefreshToken plain value: only lives in memory during the request lifetime.
  Never written to any log. Never in any response field other than the cookie.
- JWT validation: use Microsoft.IdentityModel.Tokens ValidateToken with
  ValidateIssuer=true, ValidateAudience=true, ValidateLifetime=true,
  ValidateIssuerSigningKey=true
- API key path: fmz_ prefix check happens before JWT check in middleware.
  If starts with fmz_ → treat as API key, not Bearer JWT.
- WorkspaceMembership in JWT: only active memberships (IsActive=true in WorkspaceMember)
