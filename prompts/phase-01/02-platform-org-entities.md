---
prompt-id: 02-platform-org-entities
phase: 01
sequence: 2
roles: [Executor, Verifier]
type: feature
depends-on: [01-backend-scaffold]
estimated-complexity: High
---

# Platform & Organisation Layer — Entities, Migrations, Services

## Context
Backend scaffold complete. All base infrastructure exists. Build the top two tiers:
Platform (plans) and Organisation (company + billing + owner). See FUNCTIONAL.md §4
for RBAC spec and §13 for billing limits. Read FUNCTIONAL.md §1.3 for email addresses.

## Objective
Create all platform and organisation entities, EF Core migrations, repositories, and
service layer including BCrypt password hashing, plan seeding, and email sending on
registration.

## Scope

### What to Build

**Entities (Flowamaz.Core/Entities/Platform/):**

`Plan`:
- Id (Guid), Name, Slug (unique), PriceMonthlyUsd (decimal), PriceAnnualUsd (decimal)
- Limits (owned entity / jsonb): MaxWorkspaces, MaxMembersPerWorkspace,
  MaxWorkflowDefinitions, MaxRunsPerMonth, MaxAiCallsPerMonth, MaxStorageGb,
  RunRetentionDays, ApiRateLimitPerMinute, AllowedAiProviders (List<string>)
- Features (owned entity / jsonb): Sso, Scim, CustomConnectors, AuditLog,
  ApiAccess, Byom, DataResidency, Cmek, ProcessIntelligence, RoiAnalytics
- IsActive (bool), IsPublic (bool), CreatedAt, UpdatedAt

`Organisation`:
- Id (Guid), Name, Slug (unique globally), BillingEmail, StripeCustomerId (nullable)
- PlanId (FK → Plan), Status (enum: Trial/Active/Suspended/Cancelled)
- TrialEndsAt (DateTime), DataRegion (enum: ApSoutheast1/EuWest1/UsEast1)
- CreatedAt, UpdatedAt, IsDeleted, DeletedAt

`Subscription`:
- Id (Guid), OrgId (FK), PlanId (FK)
- BillingCycle (enum: Monthly/Annual)
- CurrentPeriodStart, CurrentPeriodEnd
- OvercapCapUsd (decimal), Status (enum: Active/PastDue/Cancelled)
- CreatedAt, UpdatedAt

`OrgUser`:
- Id (Guid), OrgId (FK), Email, Name, PasswordHash
- IsOrgOwner (bool), IsActive (bool)
- LastLoginAt (DateTime?), LockoutUntil (DateTime?), FailedLoginCount (int)
- CreatedAt, UpdatedAt, IsDeleted, DeletedAt
- Index: (OrgId + Email) unique — email unique per org, NOT globally

`UsageAggregate`:
- Id (Guid), OrgId (FK), PeriodMonth (string "2026-05")
- RunsCount (long), AiCallsCount (long), AiCostUsd (decimal)
- StorageBytes (long), MemberPeak (int), ApiCallsCount (long)
- CreatedAt, UpdatedAt
- Index: (OrgId + PeriodMonth) unique

**Services (Flowamaz.Application/Platform/Services/):**

`IOrganisationService` + `OrganisationService`:
- RegisterOrganisationAsync(name, slug, billingEmail, ownerEmail, ownerName,
  ownerPassword, planSlug, dataRegion) → OrgRegistrationResult
  — Validates slug uniqueness
  — Creates Org + OrgUser (owner, BCrypt cost 12) + Subscription (trial, 14 days)
  — Sends welcome email via IEmailService to owner
  — All in one DbContext transaction
- GetByIdAsync(orgId) → Organisation?
- GetBySlugAsync(slug) → Organisation?
- GetPlanLimitsAsync(orgId) → PlanLimits

`IOrgUserService` + `OrgUserService`:
- GetByEmailAndOrgAsync(email, orgId) → OrgUser?
- ValidateCredentialsAsync(email, orgId, password) → OrgUser? (BCrypt verify)
- UpdateLastLoginAsync(orgUserId)
- RecordFailedLoginAsync(orgUserId) — increments FailedLoginCount
- IsLockedOutAsync(orgUserId) → bool (LockoutUntil > UtcNow)
- LockAccountAsync(orgUserId, duration) — sets LockoutUntil
- ResetFailedLoginCountAsync(orgUserId)

**Plan seed data (fixed Guids — idempotent on re-run):**

Community:
- MaxWorkflows=5, MaxRunsPerMonth=500, MaxMembersPerWorkspace=1, MaxWorkspaces=1
- AllowedAiProviders=["anthropic"], StorageGb=1, RunRetentionDays=7
- All features: false except none (bare minimum)

Starter ($49/mo):
- MaxWorkflows=20, MaxRunsPerMonth=5000, MaxMembersPerWorkspace=10, MaxWorkspaces=3
- AllowedAiProviders=["anthropic","azure-openai","google"], StorageGb=10, RunRetentionDays=90
- ApiAccess=true, CustomConnectors=true

Pro ($199/mo):
- MaxWorkflows=0 (unlimited), MaxRunsPerMonth=50000, MaxMembersPerWorkspace=50, MaxWorkspaces=10
- AllowedAiProviders=["anthropic","azure-openai","google","kimi","mistral"]
- StorageGb=50, RunRetentionDays=365
- ApiAccess=true, CustomConnectors=true, AuditLog=true, ProcessIntelligence=true, RoiAnalytics=true

Enterprise ($0 — custom):
- MaxWorkflows=0, MaxRunsPerMonth=0, MaxMembersPerWorkspace=0, MaxWorkspaces=0 (all unlimited)
- AllowedAiProviders=["anthropic","azure-openai","google","kimi","mistral","byom"]
- All features: true

**Email on registration** (via IEmailService):
- To: owner email
- Subject: "Welcome to Flowamaz — your 14-day trial has started"
- Body: welcome message, link to app.flowamaz.io, support@flowamaz.io contact
- Plain text fallback included

**Migration:** `AddPlatformOrganisationSchema`

### What NOT to Build
- No workspace entities — prompt 03
- No API endpoints — prompts 04-05
- No JWT token generation — prompt 04
- No password reset flow — Phase 2

## Technical Requirements
- [ ] BCrypt cost factor exactly 12 (BCrypt.Net-Next package)
- [ ] TrialEndsAt = UtcNow + 14 days on registration
- [ ] Plan seed Guids are fixed — re-running migration does not create duplicates
- [ ] OrgUser email unique per org (not globally) — composite unique index
- [ ] RegisterOrganisationAsync uses DbContext transaction — all or nothing
- [ ] Welcome email: logged as info on success, logged as warning on failure — never throws
- [ ] Serilog on all service method entry, exit, and error paths
- [ ] Unit tests: RegisterOrganisationAsync (happy, duplicate slug, duplicate email),
  ValidateCredentialsAsync (correct, wrong password, inactive user, locked out)

## Acceptance Criteria
- [ ] dotnet build — zero errors
- [ ] dotnet test — all unit tests pass
- [ ] Migration applies cleanly on fresh Postgres
- [ ] 4 plans seeded with correct limits
- [ ] BCrypt: VerifyPassword(plaintext, hash) returns true in unit test
- [ ] Duplicate org slug → throws SlugAlreadyExistsException
- [ ] RegisterOrganisationAsync rollback on failure (unit tested with mocked failing email)

## Output Expected
```
backend/Flowamaz.Core/Entities/Platform/Plan.cs
backend/Flowamaz.Core/Entities/Platform/Organisation.cs
backend/Flowamaz.Core/Entities/Platform/Subscription.cs
backend/Flowamaz.Core/Entities/Platform/OrgUser.cs
backend/Flowamaz.Core/Entities/Platform/UsageAggregate.cs
backend/Flowamaz.Core/Enums/OrgStatus.cs
backend/Flowamaz.Core/Enums/BillingCycle.cs
backend/Flowamaz.Core/Enums/DataRegion.cs
backend/Flowamaz.Core/Enums/SubscriptionStatus.cs
backend/Flowamaz.Core/Models/PlanLimits.cs
backend/Flowamaz.Core/Models/PlanFeatures.cs
backend/Flowamaz.Core/Interfaces/Services/IOrganisationService.cs
backend/Flowamaz.Core/Interfaces/Services/IOrgUserService.cs
backend/Flowamaz.Application/Platform/Services/OrganisationService.cs
backend/Flowamaz.Application/Platform/Services/OrgUserService.cs
backend/Flowamaz.Application/Platform/DTOs/OrgRegistrationRequest.cs
backend/Flowamaz.Application/Platform/DTOs/OrgRegistrationResult.cs
backend/Flowamaz.Application/Platform/Validators/OrgRegistrationValidator.cs
backend/Flowamaz.Infrastructure/Persistence/Repositories/PlanRepository.cs
backend/Flowamaz.Infrastructure/Persistence/Repositories/OrganisationRepository.cs
backend/Flowamaz.Infrastructure/Persistence/Repositories/OrgUserRepository.cs
backend/Flowamaz.Infrastructure/Persistence/Configurations/PlanConfiguration.cs
backend/Flowamaz.Infrastructure/Persistence/Configurations/OrganisationConfiguration.cs
backend/Flowamaz.Infrastructure/Persistence/Configurations/OrgUserConfiguration.cs
backend/Flowamaz.Infrastructure/Persistence/Migrations/[ts]_AddPlatformOrganisationSchema.cs
backend/Flowamaz.Tests.Unit/Platform/OrganisationServiceTests.cs
backend/Flowamaz.Tests.Unit/Platform/OrgUserServiceTests.cs
```

## Notes for Executor
- BCrypt.Net-Next NuGet package — use BCrypt.Net.BCrypt.HashPassword(password, 12)
- DataRegion default: ApSoutheast1 (Malaysia — our home market)
- Plan seed: use HasData() in OnModelCreating with fixed Guid literals
- IOrganisationService.RegisterOrganisationAsync must be atomic — use IDbContextTransaction
- Welcome email HTML: keep simple — name, link to app, support email. No fancy templates yet.
