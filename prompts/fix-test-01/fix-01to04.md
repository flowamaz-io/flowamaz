Read CLAUDE.md and FUNCTIONAL.md.
Fix the following 4 issues found during live testing. Fix all in one session.

--- FIX 1: Workspace not showing in workspace selector after registration ---

Problem: After registration, the workspace created during onboarding does not
appear in the workspace selector dropdown. User has no workspace selected.

Investigate and fix:
1. Check JwtService.GenerateAccessToken — verify it loads WorkspaceMember records
   and includes workspace claims in the JWT after workspace creation during onboarding
2. Check workspace.store — verify loadWorkspaces action calls
   GET /api/v1/workspaces and populates the store correctly
3. Check WorkspaceSwitcher.vue (or wherever the workspace dropdown renders) —
   verify it reads from workspace.store and renders all workspaces
4. The onboarding wizard creates a workspace via POST /api/v1/workspaces —
   after this call succeeds, the auth store must refresh the access token
   so the new workspace appears in JWT claims immediately
5. Fix: after workspace creation in onboarding step 1, call auth.store.refreshToken()
   then workspace.store.loadWorkspaces() before advancing to step 2

--- FIX 2: Password confirmation field missing on registration ---

File: web/src/views/auth/RegisterView.vue
Add a "Confirm Password" field below the Password field.
- Label: "Confirm password"
- Type: password
- Validation: must match password field value
- Error message: "Passwords do not match"
- Client-side validation only — no API change needed
- Use VeeValidate (already in the project) for the matching rule
- Submit button disabled until passwords match

--- FIX 3: Page refresh logs out (silent refresh not working) ---

Problem: On page refresh, the access token is lost (Pinia memory cleared) and the
silent refresh attempt fails, logging the user out.

The silent refresh calls POST /api/v1/auth/refresh which reads the httpOnly cookie
fmz_refresh. This is failing — likely because:
a) The cookie SameSite=Strict is blocking the refresh call on reload, OR
b) The cookie Path=/api/v1/auth means it is only sent to auth endpoints (correct),
   but the Axios call may not be hitting the right path, OR
c) CORS is blocking the cookie from being sent cross-origin

Fix:
1. In auth.store.ts — on app initialisation (app.vue mounted or router beforeEach),
   call refreshToken() and await it before rendering any protected route
2. Ensure the Axios refresh call includes withCredentials: true so the httpOnly
   cookie is sent cross-origin
3. In api.service.ts — ensure the Axios instance has withCredentials: true globally
4. Verify the cookie is being set correctly after login:
   Check AuthController — cookie must include SameSite=None when served cross-origin
   (frontend on :8306, backend on :8307 = different ports = cross-origin)
   Change cookie SameSite from Strict to None with Secure=false in Development
   OR change SameSite to Lax which works for same-site navigation refreshes
5. Update backend AuthController cookie settings for Development:
   SameSite = SameSiteMode.Lax (not Strict — Strict blocks cross-origin cookie sending)

--- FIX 4: Forgot Password — build the complete flow ---

Backend:
1. Add PasswordResetToken entity:
   - Id (Guid), OrgUserId (FK), TokenHash (SHA256), ExpiresAt (1 hour), UsedAt (DateTime?), CreatedAt
   - Migration: AddPasswordResetTokens

2. Add to IAuthService / AuthService:
   - RequestPasswordResetAsync(email, orgSlug, ct)
     Find org by slug, find user by email+orgId
     Generate random token (32 bytes hex), hash it, store PasswordResetToken
     Send email via IEmailService:
       Subject: "Reset your Flowamaz password"
       Body: "Click to reset: {PLATFORM_BASE_URL}/reset-password?token={plainToken}&org={orgSlug}"
       If user not found: return success anyway (no user enumeration)
     Expiry: 1 hour
   - ResetPasswordAsync(plainToken, orgSlug, newPassword, ct)
     Hash the token, find PasswordResetToken by hash where UsedAt=null and ExpiresAt > now
     Validate: token exists, not used, not expired
     Update OrgUser.PasswordHash (BCrypt cost 12)
     Mark token as used (UsedAt = UtcNow)
     Invalidate all existing refresh tokens for this user (security)
     Return success

3. API endpoints:
   POST /api/v1/auth/forgot-password
   Body: { email, org_slug }
   Returns: 200 always (no user enumeration)
   Rate limit: 3 per hour per IP (Redis counter)

   POST /api/v1/auth/reset-password
   Body: { token, org_slug, new_password }
   Returns: 200 on success, 400 on invalid/expired token
   Validates: new_password minimum 8 chars, 1 upper, 1 number

Frontend:
1. ForgotPasswordView (/forgot-password):
   - Email field + org URL field
   - Submit → POST /api/v1/auth/forgot-password
   - Success state: "If that email exists, a reset link has been sent. Check your inbox."
   - No error differentiation (no user enumeration on frontend either)

2. ResetPasswordView (/reset-password):
   - Reads ?token= and ?org= from URL query params
   - New password field + confirm password field
   - Submit → POST /api/v1/auth/reset-password
   - Success: "Password reset. Redirecting to login..." → redirect to /login after 2s
   - Error: "This reset link has expired or already been used. Request a new one."

3. LoginView update:
   - "Forgot password?" link → /forgot-password (remove "coming soon" toast)

Unit tests:
- RequestPasswordResetAsync: unknown email → returns success (no enumeration)
- RequestPasswordResetAsync: valid email → token stored, email sent
- ResetPasswordAsync: valid token → password updated, token marked used
- ResetPasswordAsync: expired token → 400
- ResetPasswordAsync: already used token → 400

After all 4 fixes:
- dotnet build — 0 errors 0 warnings
- dotnet test — all pass
- npm run build --prefix web — 0 TypeScript errors

git add . && git commit -m "fix(auth): workspace selector, password confirm, session refresh, forgot password" && git push origin develop

Then rebuild containers:
docker compose -f infrastructure/docker-compose.yml --env-file .env up -d --build backend --build web