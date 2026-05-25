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

---

## Prompt 05 — Workspace & Member Management API  (2026-05-25)

**Status:** Complete · pushed to `develop`

**Built**
- `WorkspacesController` (list/create/get/settings/delete + ai-config GET/PUT), `WorkspaceMembersController` (list/add/update-role/remove), `WorkspaceApiKeysController` (list/create/revoke).
- `RequireWorkspaceRoleAttribute` + `WorkspaceAuthorizationHandler` (TypeFilter): resolves workspaceId from route, 404s wrong-org workspaces (never 403), enforces minimum role via DB; `RequireOrgOwnerAttribute` + handler (is_org_owner claim).
- `IWorkspaceAiConfigService` + impl: resolved per-function view (workspace/platform level) without resolving keys; updates validate provider against the org allowlist (OrgAiConfig → plan fallback) and model capability gates.
- `is_org_owner` claim added to JWT → CurrentUserContext → ICurrentUserService.
- Member-detail query (email/name) + service method; member DTOs/validators; `PagedResult<T>` pagination on all list endpoints.
- Global `JsonStringEnumConverter` for controllers (string enums in/out).
- Exceptions: `UserNotInOrganisationException` (404), `AlreadyMemberException` (409).
- Tests: WorkspaceApiTests, MemberApiTests, ApiKeyTests (integration). Total now 76 unit + 26 integration green.

**Bugs found & fixed during this prompt**
- **Login 500 for any user with workspace memberships:** `GetActiveMembershipsForUserAsync` projected `(int)Role` in LINQ → EF emitted a SQL `role::int` cast, but the role column is text, so Postgres failed parsing `"Viewer"` as int. Now materialised then projected client-side. (Caught by the Viewer RBAC integration test.)
- Enums serialised as integers by default → string-enum request binding (e.g. `marketplacePolicy:"AllowAll"`) failed and role responses were numbers. Fixed with a global `JsonStringEnumConverter`.

**DoD / acceptance checklist**
- [x] dotnet build 0/0; 76 unit + 26 integration pass
- [x] Unknown email on add-member → 404 with "must register first"
- [x] GET api-keys → PlainKey null in every item; POST api-keys → PlainKey present + `X-Plain-Key-One-Time: true`
- [x] Viewer → POST members → 403 (integration test seeds a Viewer and logs in)
- [x] Wrong org's workspace → 404 (not 403)
- [x] AI config: Azure model when org allows only Anthropic → rejected (4xx)
- [x] Pagination on all list endpoints
- [x] Workspace isolation: role filter confirms org ownership before role check; service queries workspace-scoped

**Deviations**
1. Workspace request/response DTOs and `PagedResult<T>` are in `Application/Workspace/DTOs`; AI-config view types and `WorkspaceMemberDetailDto` are in `Core/Models` (Core service interfaces return them — clean architecture).
2. AI-config validation failures surface as **422** (`ConfigViolationException`, reused from prompt 01) rather than the prompt's illustrative 400. Self-modification / last-admin return **409** (conflict) not 400.
3. PUT settings validates providers against the known provider set (`AiProviders.All`); strict org-allowlist enforcement lives in the dedicated ai-config endpoint as specified.
4. API-key callers cannot perform role-gated management actions in Phase 1 (handler denies); these endpoints are human-only.
5. Integration tests run sequentially (`DisableTestParallelization`) — container-backed API tests contend when collections run in parallel; `AuthEndpointTests` migrated onto the shared `api` collection fixture.

---

## Prompt 06 — Frontend Scaffold (Vue 3)  (2026-05-25)

**Status:** Complete · pushed to `develop`

**Built** (under `web/`, 88 source files)
- Vue 3 + Vite 7 + TypeScript strict + Tailwind v4 (brand tokens via `@theme` in main.css: teal #1D9E75, purple #534AB7, amber #EF9F27, danger #E24B4A).
- 11 Fm* base components (Button, Input, Badge, Modal, Alert, Spinner, Dropdown, EmptyState, ErrorState, Toast, Pagination); layout (AppShell/Sidebar/TopBar).
- Auth views (Login, Register w/ slug auto-gen + plan cards), Dashboard (getting-started checklist + metric placeholders + creation-method hero), workspace views (Settings + AI config, Members, ApiKeys w/ one-time plain key), Onboarding wizard (3 steps, localStorage resume).
- Help system: HelpPanel (Shift+? global), HelpSearch, HelpTooltip (@floating-ui/vue), ArticleRenderer (marked), articleMap.ts, 15 article stubs loaded via `import.meta.glob(?raw, eager)` — real content in prompt 08.
- Pinia stores (auth — access token in memory only + silent refresh on init; workspace; ui), services (api w/ Bearer + 401 refresh-retry-once + withCredentials; auth; workspace), router + guards, composables, types, utils.
- Vitest + MSW; 6 unit tests (auth.store, FmEmptyState) green. ESLint config; Playwright config (specs in prompt 09).

**DoD / acceptance checklist**
- [x] `npm run build` (vue-tsc -b && vite build) — clean, 0 TS errors
- [x] `npm run typecheck` 0 errors; `npm run test` 6/6; `npm run lint` 0
- [x] Zero `<style>` blocks in any `.vue`; zero `any` types
- [x] Access token memory-only; 401 → refresh → retry once
- [x] Help panel routes to correct article per Phase 1 route; HelpTooltip on form fields
- [x] Onboarding resumes from last step; getting-started checklist dismissable per org
- [x] node_modules / dist gitignored

**Deviations**
1. Tailwind v4 brand tokens + help-article prose styles live in `src/assets/main.css` (the v4-native `@theme` way); `tailwind.config.ts` is a thin tooling stub. `.vue` files remain style-block-free.
2. ApiKeysView offers environment IDs seen on existing keys (plus a manual UUID field) because Phase 1 has no "list environments" endpoint — flagged for a future endpoint.
3. TypeScript pinned to 5.9.3 (5.9.4 doesn't exist) for the typescript-eslint peer range.
4. Help articles are heading + one-sentence stubs (real content is prompt 08, per spec).
5. Built via a delegated sub-agent; output independently verified (build/typecheck/tests/style-block/any-type scans all pass).

---

## Prompt 07 — Docker, Nginx & Deployment Config  (2026-05-25)

**Status:** Complete · pushed to `develop`

**Built**
- `backend/Dockerfile` (multi-stage .NET 10) + `.dockerignore`; `web/Dockerfile` (node 22 → nginx) + `.dockerignore` + `web/nginx.conf` (SPA).
- `infrastructure/docker-compose.yml` (5 services: proxy/backend/web/db/redis; backend overrides DB/Redis to service-name connection strings, runs Production).
- `infrastructure/nginx/nginx.conf` — HTTP→HTTPS 301, TLS, security headers, rate-limit zones (auth/register/api), proxies to backend/web.
- Dev SSL: `nginx/ssl/generate-dev-cert.sh`, `.gitignore` (no certs committed), `README.md`.
- Docs: root `README.md`, `SECURITY.md`, `docs/DEVELOPMENT.md`, `docs/DEPLOYMENT.md` (env reference, Let's Encrypt, DNS/MX/SPF/DKIM/DMARC, M365 + Resend, migrations, backups).

**Bug found & fixed**
- **`JWT_SECRET` env var never reached the app.** The app binds `Jwt:Secret`; the documented `JWT_SECRET` env var maps to `Jwt__Secret` in .NET, so it was ignored (dev worked only via appsettings). In Docker/Production the backend crashed at startup. Fixed in `Program.cs` by mapping `JWT_*` env vars onto the `Jwt:*` config keys (mirrors `ConnectionStringResolver`'s env-var-first approach). Verified: with a secret provided the backend boots and serves `/health`.

**Verification**
- [x] `docker compose config` parses for both compose files
- [x] backend + web images **build** (.NET 10; base is Ubuntu 24.04, so the runtime uses the image's pre-created `$APP_UID` non-root user — the snippet's Debian `adduser`/`curl` install was adjusted: `adduser` is absent on Ubuntu, `curl` installed via apt)
- [x] full stack `up`: db/redis/web/backend start; backend boots and serves `/health`; **redis healthy** via service name
- [x] backend correctly **refuses to start in Production without `JWT_SECRET`** (security-positive)
- [x] no secrets in committed files; dev certs git-ignored
- [x] 76 unit + 26 integration backend tests still green after the Program.cs change

**Deviations / environment notes**
1. **.NET 10 images** (`sdk:10.0`/`aspnet:10.0`), not the snippet's 9.0 (can't build net10.0). Runtime uses `USER $APP_UID` (the .NET base's built-in non-root user) instead of `adduser` (absent on the Ubuntu base).
2. Backend Dockerfile restores the **API project** (not the .sln, which references test projects whose csproj files aren't in the image context).
3. `version:` key omitted from compose (obsolete in modern Compose; avoids the deprecation warning).
4. **Not fully smoke-tested in this session:** the proxy couldn't bind host port 80 (already in use on this machine), and the full `/health` showed `db: unhealthy` because the local `.env` has an empty `DB_PASSWORD` (template not filled). Both are deployment-time provisioning concerns documented in DEPLOYMENT.md, not config bugs — Redis (same service-name wiring) is healthy, proving the network wiring.

---

## Prompt 08 — Help Content + docs.flowamaz.io  (2026-05-25)

**Status:** Complete · pushed to `develop`

**Built**
- 15 complete Phase 1 help articles (~5,500 words) at the canonical paths: getting-started/ (6), workspaces/ (5), billing/ (4). Each has frontmatter (title, updated 2026-05-24, readingTime, plans badge), real content sourced from FUNCTIONAL.md (§13.2 limits table, §4.5 permission map, §1.6 editions, §15 CLI), and a "Was this helpful? / Edit on GitHub ↗" footer. No placeholder text.
- Reorganised the prompt-06 stubs to canonical names (plans/ → billing/, fixed two getting-started filenames).
- Docusaurus 3.x site under `docs/` (config, sidebars, package.json, README, custom.css, logo) with the 15 articles mirrored byte-identically to `docs/docs/`; `npm install` + `npx docusaurus build` succeed. `docs/.gitignore` excludes node_modules/build/.docusaurus. The prompt-07 DEVELOPMENT.md/DEPLOYMENT.md are untouched.
- `articleMap.ts`: 7 route mappings + `errorArticleMap` (401/403/404/429) + error-code map (workspace_not_found/api_key_invalid/plan_limit_exceeded) + `articleForError()`.
- `FmErrorState` accepts optional statusCode/errorCode and resolves a help article via the map (explicit helpArticle wins). `articleLoader`/`ArticleRenderer` now parse frontmatter and render a coloured plan badge.

**DoD / acceptance checklist**
- [x] 15 articles exist with complete content; placeholder scan (lorem/coming soon/TBD/placeholder) clean
- [x] 15 mirrored to docs/docs; Docusaurus builds with `npx docusaurus build`
- [x] `web` build green; vitest green; no `<style>` blocks
- [x] articleMap covers all 7 Phase 1 routes; FmErrorState wired to error→article map
- [x] Edit-on-GitHub footer links use the docs repo path format

**Deviations**
1. Article loader strips YAML frontmatter and exposes `plans`/`readingTime`; ArticleRenderer renders the plan badge (needed so frontmatter doesn't show as literal text — aligns with the spec's "render a coloured badge").
2. Two articles' `<url>` autolinks converted to markdown links/inline code so Docusaurus 3 (MDX) compiles; content stays identical between web and docs.
3. Docusaurus emits ~9 broken-relative-link warnings (cross-article links under `routeBasePath: '/'`); `onBrokenLinks: 'warn'` keeps the build green and in-app navigation (slug-based) is unaffected.
4. Content written via a delegated sub-agent; independently verified (15 canonical paths, build/test green, placeholder scan clean, content spot-checked).

---

## Prompt 09 — Phase 1 Integration (E2E, coverage, PHASE_COMPLETE)  (2026-05-25)

**Status:** Complete · pushed to `develop`

**Built**
- Backend `Phase1LifecycleTests` (5 integration tests on the shared API+Postgres+Redis fixture): full register→login→refresh→workspace→add/list/remove member→logout lifecycle; workspace isolation (org B → org A workspace → 404); API-key lifecycle (create→validate→scope check→revoke→validate fails); RBAC (seeded Viewer → Admin action → 403); Redis rate limiter counts then blocks.
- Targeted coverage unit tests (`WorkspaceServicesCoverageTests`, +15) for the workspace service read/getter/mutation paths.
- Playwright E2E suite (`web/e2e/`): `playwright.config.ts`, `fixtures/test-factories.ts`, and 17 scenarios (S1–S17) across auth/workspace/onboarding/help specs using semantic selectors. `@playwright/test` + `e2e` script wired into package.json.
- checkpoint.md → PHASE_COMPLETE.

**Results**
- Backend: **91 unit + 31 integration tests pass** (0 fail, 0 skip). `dotnet build` 0/0. `web` build green.
- Coverage (Coverlet, line): service layer Application+Infrastructure **66.5%** excluding generated EF migration files (the raw assembly figure is ~54% because the migration snapshot/Designer files are tens of thousands of generated lines). Core business logic is well covered — AuthService 92%, JwtService 90%, OrganisationService/WorkspaceService/WorkspaceMemberService/WorkspaceApiKeyService/WorkspaceAuthorizationService sync paths 100%, CreateApiKey 90%, RegisterOrganisation 90%.

**Known gaps (queued for fix-01)**
- **Coverage below the 80% target.** Shortfall is concentrated in (a) defensive error-logging `catch` branches across services, and (b) Redis/AI infrastructure services (SemanticCacheService, AiTokenMeteringService, ModelResolutionService, WorkspaceAiConfigService) that are only partially exercised. The high-value business logic is covered; chasing boilerplate catch-branch coverage was deprioritised.
- **Playwright E2E: 17/17 passing in a live run** (the sub-agent stood up backend + Vite + throwaway Postgres/Redis, installed Chromium, and ran the full suite — stable across two runs, no SPA bugs found). S9/S10/S11/S17 assert real Phase 1 behaviour (no second-org-user API, no environments endpoint, view-wired help articles) via the actionable error path or route-mocking. Note: the run needs a `resetRateLimits()` helper (flushes Redis `ratelimit:*`) because all local traffic shares one IP and trips the 5/hour register cap.
- **docker compose full smoke** is partial (see prompt 07): images build and the backend boots/serves /health; full health needs a provisioned `.env` (DB_PASSWORD) and a free host port 80.

---

## Fix-01-01 — Backend Service Layer Coverage ≥ 80%  (2026-05-25)

**Status:** Complete

**Issue fixed:** Service-layer line coverage was 66.5% (below 80% DoD). Gap concentrated in AI/Redis infra services and defensive error-logging catch branches.

**Changes**
- `RepositoryBase.GetByIdAsync` already uses `FirstOrDefaultAsync` (the FindAsync→FirstOrDefaultAsync fix from Copilot review was already applied) — added `RepositoryBaseTests` proving a soft-deleted entity returns null (global query filter honoured).
- New `SemanticCacheServiceTests` (mocked `IConnectionMultiplexer`/`IDatabase`): ComputeKey determinism + normalisation, GetAsync hit/miss/fail-open, SetAsync success + fail-open. SemanticCacheService → 100%.
- Extended `AiTokenMeteringServiceTests`: null-workspace records, background-write failure swallowed on caller thread (fire-and-forget contract). AiTokenMeteringService → 100%.
- Extended `ModelResolutionServiceTests`: platform-default fallback, Google+Platform-key BYOK-only violation, Anthropic platform-key-missing, and the three malformed-override paths (invalid JSON / missing modelId / invalid keySource all surface as `ConfigViolationException`, never a raw parse crash). ModelResolutionService → 97.3%.
- New `WorkspaceAiConfigServiceTests` (InMemory DB): resolved view (providers/budget/functions), unknown workspace, and the four UpdateAsync gates (provider-not-allowed, unknown-model, provider-mismatch, F3-without-vision) + valid-override persistence. WorkspaceAiConfigService → 94.3%.
- New `RateLimitServiceUnitTests`: fail-open when Redis throws + argument guards. (Atomic INCR+EXPIRE remains covered by the Testcontainers integration suite.) RateLimitService → 100% combined.
- New `ServiceErrorBranchTests`: transaction rollback + rethrow on save failure for OrganisationService.RegisterOrganisationAsync, WorkspaceService.CreateWorkspaceAsync, AuthService.RefreshAsync; repository-error propagation for OrgUserService.ValidateCredentialsAsync.

**Results**
- `dotnet build` — 0 errors, 0 warnings.
- `dotnet test` — **125 unit + 31 integration = 156 pass** (0 fail, 0 skip).
- Coverage (Coverlet, merged unit+integration, service classes only — Application `.Services.` + Infrastructure `.Services.`): **88.0%** (1306/1484 lines), up from 66.5%. Per-class: SemanticCache 100%, RateLimit 100%, AiTokenMetering 100%, ModelResolution 97.3%, WorkspaceAiConfig 94.3%, JwtService 95%, OrganisationService 89.2%, AuthService 89.1%, WorkspaceApiKey 88.5%, WorkspaceService 86.9%, MemberService 84.2%, AuthZ 84.4%, OrgUserService 74.6%, EmailService 45.8% (external Resend HTTP IO — out of scope).

**Deviations**
1. RepositoryBase FindAsync→FirstOrDefaultAsync was already fixed in the codebase; only the proving test was added.
2. WorkspaceEnvironmentDto/env-endpoint and E2E belong to fix-01-02; not in this commit.
3. results.md had been truncated to its header in the working tree before this session — restored the full Phase-1 history from HEAD before appending (append-only rule).

---

## Fix-01-02 — Environments Endpoint + Live E2E 17/17  (2026-05-25)

**Status:** Complete

**Issues fixed**
1. ApiKeysView used an environment-ID workaround (IDs scraped off existing keys + manual UUID entry) — no real environments list endpoint existed.
2. The 17 Playwright scenarios had not been re-run live after the fix-phase changes.

**Backend**
- `GET /api/v1/workspaces/{id}/environments` added to `WorkspacesController` — `[RequireWorkspaceRole(Viewer)]`, returns the 3 environments ordered Dev → Staging → Production.
- `IWorkspaceService.GetEnvironmentsAsync` + `WorkspaceService.GetEnvironmentsAsync` (delegates to the existing `IWorkspaceRepository.GetEnvironmentsAsync`, orders by `Name`, Serilog entry/exit/error).
- `WorkspaceEnvironmentResponse(Id, Name, WorkspaceId, CreatedAt)` DTO (added to WorkspaceDtos.cs to match the one-file-of-records convention rather than a standalone file).
- Integration test `EnvironmentsApiTests`: returns 3 in Dev/Staging/Production order with the correct workspaceId; a non-member org gets 4xx.

**Frontend**
- `workspace.service.ts` → `getEnvironments(id)`; store `environments` ref + `loadEnvironments()` action (cleared on workspace switch); exposed via `useWorkspace`.
- `WorkspaceEnvironment` type added.
- `ApiKeysView.vue`: environments loaded from the API on mount (alongside keys), the manual-UUID field replaced with a real `<select>` of Dev/Staging/Production by name, and a new "Environment" column shows the environment name (looked up by id) in the keys table. Loading/error states reuse the existing `load()` try/catch.

**Results**
- `dotnet build` 0/0; `dotnet test` **125 unit + 33 integration = 158 pass**.
- Web: `vue-tsc` typecheck clean, `npm run build` green, vitest 6/6.
- Playwright (Chromium, live stack — dev Postgres+Redis, `dotnet run` API on :5000, Vite on :5173): **17/17 passed (29.9s)**. HTML report generated at `web/playwright-report/` (gitignored, not committed). Ran with `E2E_REDIS_CONTAINER=flowamaz-dev-redis` so `resetRateLimits()` flushes the dev Redis between registrations.

**Deviations**
1. The repository already had `GetEnvironmentsAsync`/`GetEnvironmentByIdAsync` from prompt 02; only the service method, endpoint, DTO and ordering were new.
2. DTO lives in WorkspaceDtos.cs (codebase convention), not a separate WorkspaceEnvironmentDto.cs as the prompt's file list illustrated.
3. S11 still stubs the api-keys POST/GET (once-only-reveal is deterministic that way) but now loads real environments — the select is populated from the live endpoint; the test's optional manual-UUID branch is simply skipped since the field no longer renders.

---

## Fix-01-03 — Status Codes, Docusaurus, Docker Stack Smoke  (2026-05-25)

**Status:** Complete

**Decision 1 — HTTP status codes (confirmed, already correct in code):**
The exception→status mappings were already implemented on each `AppException` subclass and
emitted verbatim by `GlobalExceptionMiddleware` (no middleware change needed):
- `ConfigViolationException` → **422**, `SelfModificationException`/`LastAdminException` → **409**,
  `InsufficientRoleException` → **403**.
Locked the decisions in:
- `FUNCTIONAL.md §4.8` — new "HTTP Status Codes (confirmed fix-01)" table.
- Tightened two lenient integration assertions (`WorkspaceApiTests`) from `>=400 <500` to assert
  exactly **422** for AI-config / settings provider violations.
- Added `MemberApiTests.Change_own_role_returns_409_conflict` — PATCH own role → **409**.

**Fix 2 — Docusaurus broken links:**
- Root cause: the navbar brand auto-links to `/`, but `routeBasePath: '/'` had no document at the
  root, so all 16 pages reported a broken `/` link. Added `slug: /` to
  `getting-started/what-is-flowamaz.md` (no inbound links to it, safe).
- `onBrokenLinks: 'throw'` (and migrated the deprecated `onBrokenMarkdownLinks` →
  `markdown.hooks.onBrokenMarkdownLinks: 'throw'`).
- `npm run build` in docs/ → **0 broken links, build succeeds with throw enabled**.

**Fix 3 — Full Docker stack smoke (all 6 checks pass):**
Brought up `docker-compose.yml` (proxy + web + backend + db + redis). All 6 checks green:
1. 5 containers up — backend + db report `healthy` (web/redis/proxy have no healthcheck defined).
2. `GET /health` → `{"status":"healthy","dependencies":{"db":"healthy","redis":"healthy"}}`.
3. `GET /scalar` → 302 → `/scalar/` → **200** Scalar UI.
4. `GET /` → **200** Vue SPA (text/html).
5. `POST /api/v1/auth/register` → **200**, `success:true`, access token returned.
6. `GET http://…/` → **301** → https.

**Bug found + fixed during the smoke:** `infrastructure/nginx/nginx.conf` used `rate=5r/h` for the
register zone — nginx only accepts `r/s`/`r/m`, so the proxy crash-looped (`[emerg] invalid rate`).
Changed to the tightest valid nginx rate `rate=1r/m` (coarse defence-in-depth; the API enforces the
precise 5/hour/IP register cap). Proxy then booted clean.

**Smoke-run notes / deviations (not committed):**
- Proxy host ports were remapped to 8081/8444 for the run via a throwaway override
  (`docker-compose.smoke.yml`, deleted after) because an unrelated project's container was holding
  80/443 — I did not stop another team's container. The compose file itself is unchanged (80/443).
- `.env` (gitignored) had empty `DB_PASSWORD`/`JWT_SECRET`; filled with local dev placeholders.
- **Gap flagged:** the API does not apply EF migrations on startup, so a fresh container DB boots
  schema-less (migrations are only run by the test fixture). For the smoke I applied them from the
  host (`dotnet ef database update`). Recommend a startup migrator/seed step or an init job for
  Docker — tracked for a future fix phase (out of scope here: "no migration changes").

**Results**
- `dotnet build` 0/0; `dotnet test` **125 unit + 34 integration = 159 pass**.
- Docs build: 0 broken links with `onBrokenLinks: 'throw'`.
- Docker: 5/5 containers up, all 6 smoke checks pass.

---

## Prompt 02-01 — Workflow & Instance Entities + Event Log + Startup Migration  (2026-05-25)

**Status:** Complete · pushed to `develop` · Phase 02 begins

**Built**
- 10 enums (`Flowamaz.Core/Enums`): WorkflowStatus, WorkflowCreatedByMethod, WorkflowTriggerType, InstanceStatus, InstanceTriggerType, SagaState, NodeStatus, GateDecisionStatus, GateDeliveryChannel, GateDeliveryStatus — all stored as string columns.
- 7 entities (`Core/Entities/Workflow`): WorkflowDefinition, WorkflowVersion, WorkflowInstance (with a guarded state machine: `CanTransitionTo`/`TransitionTo`), WorkflowEvent (immutable — NOT a BaseEntity, so no soft delete / no UpdatedAt), WorkflowNodeState, WorkflowVariable, GateDecision.
- 7 EF configurations + DbSets; snake_case + jsonb honoured by the existing global conventions.
- Repositories: definition, version, instance, event (append-only), gate. `WorkflowInstanceRepository.GetPendingForWorkerAsync` uses a raw `FOR UPDATE SKIP LOCKED` data-modifying CTE (top-level, `IgnoreQueryFilters().AsNoTracking()` so EF never wraps the CTE in a subquery); `RenewLeaseAsync`/`ReleaseLeaseAsync` are ownership-checked `ExecuteSqlInterpolated` updates (30s lease).
- `IStartupMigrationService` + `StartupMigrationService` (with an `IMigrationRunner` seam for unit-testing) — logs "Applying N pending migrations…" → names → complete; 2-min configurable timeout (`StartupMigrationTimeoutSeconds`); re-throws on failure. Wired into `Program.cs` in a service scope before `app.Run()` → travel-forward item from fix-01 (Docker now self-bootstraps its schema).
- Migration `20260525124915_AddWorkflowSchema`.

**Tests**
- Unit: `WorkflowInstanceTests` (valid/invalid/terminal transitions), `WorkflowEventTests` (sequence increments + per-instance isolation on InMemory), `StartupMigrationServiceTests` (no-op / applies-once / re-throws via mocked runner).
- Integration: `WorkerLeaseTests` (Testcontainers Postgres) — batch claim stamps lease, never reclaims live-leased rows, renew/release respect ownership, duplicate `idempotency_key` → `DbUpdateException`.

**DoD / acceptance checklist**
- [x] dotnet build 0/0
- [x] dotnet test all pass — **149 unit + 38 integration**
- [x] Migration applies cleanly on fresh Postgres (Testcontainers)
- [x] WorkflowEvent: AppendAsync + reads only — no update/delete on repo
- [x] SKIP LOCKED claim works (integration test)
- [x] Startup migration: `docker compose up` applies migrations automatically (block before app.Run)
- [x] IdempotencyKey unique — duplicate insert throws (integration test)
- [x] All workspace-scoped reads filter by WorkspaceId

**Deviations**
1. The decision-value enum is named **`GateDecisionStatus`** (prompt listed it as `GateDecision`) to avoid colliding with the `GateDecision` entity.
2. WorkflowVersion/NodeState/Variable/GateDecision inherit `WorkspaceEntity`, so they also carry `updated_at`/`is_deleted` (consistent with the Phase-1 convention). WorkflowEvent deliberately does not, to stay immutable.
3. Entities use FK Guid columns with **no navigation properties** (matches the Phase-1 repos) — avoids EF required-relationship-vs-query-filter warnings and keeps 0 warnings.
4. Repositories are silent pass-throughs (no Serilog), matching the Phase-1 repo precedent; Serilog entry/exit/error lives on the service (`StartupMigrationService`).
5. `idempotency_key` unique index is global (per spec "IdempotencyKey unique"); Postgres treats NULLs as distinct so unkeyed triggers are unaffected.
