# Flowamaz — Execution Results

PM Agent appends after every prompt completes. Never overwrite — only append.

---

## Prompt 02-platform-org-entities — Platform & Organisation Layer

Date: 2026-05-25
Phase: 01 · Sequence: 2 · Type: feature
Branch: develop (pushed with --force-with-lease per session protocol)

### Summary
Built tiers 1–2 of the four-tier hierarchy: Plan (reference data) and Organisation
(company + billing owner + trial subscription). Added entities, EF configs, repositories,
a unit-of-work abstraction, BCrypt-backed registration with a transactional all-or-nothing
write + post-commit welcome email, and the `AddPlatformOrganisationSchema` migration seeding
the four plans (Community/Starter/Pro/Enterprise) with fixed Guids.

### Definition of Done (per prompt)
- [x] All declared output files exist (see deviations for 2 relocations)
- [x] dotnet build — 0 errors, 0 warnings (TreatWarningsAsErrors on)
- [x] dotnet test — 29 unit tests pass (18 new: Organisation + OrgUser services)
- [x] Migration applies cleanly on Postgres 16 (verified against dev container)
- [x] 4 plans seeded with correct limits (verified via psql)
- [x] BCrypt cost factor exactly 12 — hash verifies plaintext in unit test ($12$ in hash)
- [x] Duplicate org slug → throws SlugAlreadyExistsException (409); no transaction opened
- [x] RegisterOrganisationAsync atomic — rollback on persistence failure (unit tested)
- [x] Serilog entry/exit/error on all new service methods (no credential/password in logs)
- [x] Welcome email: info on success, warning on failure, never throws
- [x] Workspace isolation: N/A this prompt (no workspace-scoped entities yet — prompt 03)
- [x] Credential values never logged (PasswordHash never passed to a log statement)
- [x] Scalar at /scalar returns 200 (UI at /scalar/, /scalar → 302 redirect; health 200)
- [x] Result logged here · checkpoint.md updated

### Acceptance criteria — all met
build=0err · tests=29 pass · migration applies clean · 4 plans seeded ·
BCrypt verify=true · duplicate slug throws · rollback on mocked failure verified.

### Architecture note
Prompt 01 placed DB-using services in Infrastructure (direct DbContext). Prompt 02 declares
`OrganisationService`/`OrgUserService` in the Application layer, which references only Core.
To keep the clean-architecture dependency direction (Application never references
Infrastructure) the following were introduced in Core and implemented in Infrastructure:
- `Core/Interfaces/Repositories/` — IPlanRepository, IOrganisationRepository, IOrgUserRepository
- `Core/Interfaces/Persistence/IUnitOfWork.cs` (+ IUnitOfWorkTransaction) — EF transaction boundary
- Infrastructure: PlanRepository, OrganisationRepository, OrgUserRepository, EfUnitOfWork
BCrypt.Net-Next (4.0.3) added to the Application project (password hashing is app logic).

### Deviations from the prompt's "Output Expected" file list
1. `OrgRegistrationResult` placed in `Core/Models/` (not `Application/Platform/DTOs/`).
   Reason: the Core `IOrganisationService` returns it, and Core cannot depend on Application.
   `OrgRegistrationRequest` stays in `Application/Platform/DTOs/` (used by the validator and
   the future endpoint). The service signature uses the prompt's positional parameters.
2. EF configurations use `IEntityTypeConfiguration` classes (as the prompt's output list
   names them) applied via `ApplyConfigurationsFromAssembly`; prompt 01's AI tables remain
   configured inline in the DbContext. Two extra configs were added beyond the declared three
   (SubscriptionConfiguration, UsageAggregateConfiguration) for the two remaining entities.
3. FK columns (PlanId, OrgId) are scalar Guid + index without DB-level FK constraints, matching
   prompt 01's convention of scalar keys over EF navigations (e.g. OrgAiConfig.OrgId). This also
   avoids the soft-delete query-filter / required-navigation interaction warning.
4. Plan/Subscription/UsageAggregate are plain entities (not BaseEntity) — the prompt's field
   lists omit IsDeleted/DeletedAt for them; only Organisation and OrgUser are soft-deletable.
   Their CreatedAt/UpdatedAt are stamped via the existing StampConfigEntity path in DbContext.

### Interpretation note
The acceptance line "rollback on failure (unit tested with mocked failing email)" conflicts with
the technical requirement "welcome email … never throws". Resolved per the technical requirement:
the email is sent AFTER commit and a failure only logs a warning (registration is not rolled
back). Two tests cover the space: (a) a mocked failing email still yields a committed
registration; (b) a mocked persistence failure rolls the transaction back and propagates.

### Files added/changed
Core: Enums/{OrgStatus,BillingCycle,DataRegion,SubscriptionStatus}.cs ·
Models/{PlanLimits,PlanFeatures,OrgRegistrationResult}.cs ·
Entities/Platform/{Plan,Organisation,Subscription,OrgUser,UsageAggregate}.cs ·
Interfaces/Repositories/{IPlanRepository,IOrganisationRepository,IOrgUserRepository}.cs ·
Interfaces/Persistence/IUnitOfWork.cs · Interfaces/Services/{IOrganisationService,IOrgUserService}.cs ·
Exceptions/SlugAlreadyExistsException.cs
Application: Platform/Services/{OrganisationService,OrgUserService}.cs ·
Platform/DTOs/OrgRegistrationRequest.cs · Platform/Validators/OrgRegistrationValidator.cs ·
DependencyInjection.cs · (csproj: + BCrypt.Net-Next, + Logging.Abstractions)
Infrastructure: Persistence/Configurations/{Plan,Organisation,OrgUser,Subscription,UsageAggregate}Configuration.cs ·
Persistence/Repositories/{Plan,Organisation,OrgUser}Repository.cs · Persistence/EfUnitOfWork.cs ·
Persistence/JsonColumn.cs · Persistence/Seed/PlatformSeedData.cs ·
Persistence/Migrations/20260525024736_AddPlatformOrganisationSchema.* ·
Persistence/FlowAmazDbContext.cs (DbSets, ApplyConfigurations, stamping) · DependencyInjection.cs (AddRepositories)
Api: Program.cs (AddApplication replaces manual validator scan)
Tests: Tests.Unit/Platform/{OrganisationServiceTests,OrgUserServiceTests}.cs

---
