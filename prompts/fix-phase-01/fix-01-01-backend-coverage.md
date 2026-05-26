---
prompt-id: fix-01-01-backend-coverage
phase: fix-phase-01
sequence: 1
roles: [Executor, Verifier, Testing]
type: fix
source-issue: Testing Agent — Phase 01 report
severity: High
depends-on: []
---

# Fix: Backend Service Layer Coverage Below 80%

## Issue Being Fixed

From phase-01-report.md:

```
ISSUE: Service-layer line coverage below the 80% DoD target
FILE: Flowamaz.Application + Flowamaz.Infrastructure (service classes)
SEVERITY: High
FINDING: 66.5% line coverage excluding generated EF migrations. Core business
logic is well covered; the gap is defensive error-logging catch branches and
the Redis/AI infra services (SemanticCache, AiTokenMetering, ModelResolution,
WorkspaceAiConfig).
FIX REQUIRED: Add unit tests for the AI/Redis services and error paths in fix-01.
```

Also from Copilot PR #1 review:
- RateLimitService has no tests for its Lua script / atomic INCR+EXPIRE behaviour
- SemanticCacheService public methods (ComputeKey, GetAsync, SetAsync) untested
- GetByIdAsync uses FindAsync which bypasses global query filters (soft-delete)

## Objective

Raise service-layer line coverage from 66.5% to ≥ 80% by adding focused unit
and integration tests for all AI/Redis infrastructure services and their error paths.
Also fix the FindAsync bypass discovered by Copilot review.

## Scope

### What to Fix and Test

**Fix first — RepositoryBase.GetByIdAsync (FindAsync bypasses soft-delete filter):**
Replace `Set.FindAsync([id], ct)` with `Set.FirstOrDefaultAsync(x => x.Id == id, ct)`
in RepositoryBase. This ensures the global soft-delete query filter is always honoured.
Add a unit test proving soft-deleted entities are not returned by GetByIdAsync.

**Unit tests to write (Flowamaz.Tests.Unit/Ai/):**

SemanticCacheServiceTests:
- ComputeKey_SameInputs_ReturnsSameHash — deterministic key for same function+workspace+command
- ComputeKey_DifferentCommands_ReturnsDifferentHash
- ComputeKey_NormalisesWhitespaceAndCase — "Add timeout" == "add  timeout"
- GetAsync_CacheHit_ReturnsValue — mock IConnectionMultiplexer, StringGetAsync returns value
- GetAsync_CacheMiss_ReturnsNull — StringGetAsync returns RedisValue.Null
- GetAsync_RedisThrows_ReturnsNullAndLogsError — exception caught, returns null, never throws
- SetAsync_Success_CallsStringSetWithCorrectTtl
- SetAsync_RedisThrows_LogsErrorAndDoesNotThrow — fail-open contract

AiTokenMeteringServiceTests:
- RecordUsageAsync_DoesNotBlockCallingThread — fire-and-forget, returns before DB write
- RecordUsageAsync_ValidInput_WritesToDatabase (use InMemory or mock repository)
- RecordUsageAsync_RepositoryThrows_LogsErrorAndDoesNotThrow — never propagates
- RecordUsageAsync_NullWorkspaceId_StillRecords

ModelResolutionServiceTests (extend existing):
- ResolveModelConfigAsync_WorkspaceOverride_WinsOverOrg
- ResolveModelConfigAsync_OrgOverride_WinsOverPlatform
- ResolveModelConfigAsync_PlatformDefault_UsedWhenNoOverrides
- ResolveModelConfigAsync_F3WithNonVisionModel_ThrowsConfigViolation
- ResolveModelConfigAsync_F7WithShortContextModel_ThrowsConfigViolation
- ResolveModelConfigAsync_GoogleProviderWithPlatformKeySource_ThrowsConfigViolation
- ResolveModelConfigAsync_ParseOverride_InvalidJson_ThrowsConfigViolationNotJsonException
- ResolveModelConfigAsync_ParseOverride_MissingModelId_ThrowsConfigViolation
- ResolveModelConfigAsync_ParseOverride_InvalidKeySource_ThrowsConfigViolation

RateLimitServiceTests (Flowamaz.Tests.Integration/Ai/ — needs real Redis via Testcontainers):
- CheckAndIncrementAsync_FirstCall_ReturnsAllowedAndSetsTtl
- CheckAndIncrementAsync_CallsUpToMax_AllAllowed
- CheckAndIncrementAsync_CallAtMaxPlusOne_Denied
- CheckAndIncrementAsync_AfterWindowExpires_CountResets — wait for TTL, try again
- CheckAndIncrementAsync_AtomicityVerified_TtlAlwaysSet — even on concurrent calls
- CheckAndIncrementAsync_RedisDown_FailsOpen (mock Redis to throw, verify returns true)

WorkspaceAiConfigServiceTests (extend existing):
- GetResolvedConfigAsync_WorkspaceLocked_CannotBeOverridden
- UpdateFunctionOverrideAsync_ProviderNotInOrgAllowlist_ThrowsConfigViolation
- UpdateFunctionOverrideAsync_GoogleWithPlatformSource_ThrowsConfigViolation
- UpdateFunctionOverrideAsync_F3WithoutVision_ThrowsConfigViolation

Error branch tests (catch paths in existing services):
For each service that has a try/catch with error logging but no test:
- Simulate the exception path, verify the error is logged, verify the
  appropriate exception is re-thrown or swallowed per the contract.
Focus on: OrganisationService.RegisterOrganisationAsync (transaction failure),
OrgUserService.ValidateCredentialsAsync (DB error), WorkspaceService.CreateWorkspaceAsync
(transaction failure), AuthService.RefreshTokenAsync (concurrent revocation).

**Coverage measurement:**
After all tests written and passing:
```bash
dotnet test --collect:"XPlat Code Coverage" \
  -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.ExcludeByFile="**/Migrations/**"
```
Run the coverage report and confirm ≥ 80% line coverage on:
- Flowamaz.Application (service classes only, exclude DTOs/validators)
- Flowamaz.Infrastructure/Services (exclude repository boilerplate)

### What NOT to Change
- No entity schema changes
- No migration changes
- No API endpoint changes
- No frontend changes
- Only tests and the FindAsync → FirstOrDefaultAsync fix

## Technical Requirements
- [ ] RepositoryBase.GetByIdAsync uses FirstOrDefaultAsync (not FindAsync)
- [ ] Soft-delete bypass unit test: create entity, soft-delete it, GetByIdAsync returns null
- [ ] SemanticCacheService: all 8 tests written and passing
- [ ] AiTokenMeteringService: all 4 tests written and passing
- [ ] ModelResolutionService: all 9 new tests written and passing
- [ ] RateLimitService: all 6 integration tests written and passing (Testcontainers Redis)
- [ ] WorkspaceAiConfigService: all 4 new tests written and passing
- [ ] Error branch tests for 4 existing services
- [ ] dotnet build — 0 errors, 0 warnings
- [ ] dotnet test — all tests pass (will be 120+ total)
- [ ] Coverage ≥ 80% confirmed by Coverlet report

## Acceptance Criteria
- [ ] GetByIdAsync returns null for soft-deleted entity (proven by test)
- [ ] SemanticCacheService.GetAsync: Redis exception → returns null, never throws
- [ ] AiTokenMeteringService: exception in fire-and-forget → logged, never propagates
- [ ] ModelResolutionService: invalid JSON in jsonb column → ConfigViolationException not 500
- [ ] RateLimitService: INCR+EXPIRE is atomic (TTL always set even on first call)
- [ ] Coverage report shows ≥ 80% on service classes

## Output Expected
```
backend/Flowamaz.Infrastructure/Persistence/RepositoryBase.cs (FindAsync fix)
backend/Flowamaz.Tests.Unit/Ai/SemanticCacheServiceTests.cs
backend/Flowamaz.Tests.Unit/Ai/AiTokenMeteringServiceTests.cs
backend/Flowamaz.Tests.Unit/Ai/ModelResolutionServiceTests.cs (extended)
backend/Flowamaz.Tests.Unit/Ai/WorkspaceAiConfigServiceTests.cs (extended)
backend/Flowamaz.Tests.Integration/Ai/RateLimitServiceIntegrationTests.cs
backend/Flowamaz.Tests.Unit/Platform/ (error branch tests for existing services)
```

## Notes for Executor
- Mock IConnectionMultiplexer for SemanticCache/RateLimitService unit tests using Moq.
  For RateLimitService atomicity tests, use real Redis via Testcontainers.
- AiTokenMeteringService fire-and-forget test: use a TaskCompletionSource or a
  small delay to let the background task complete before asserting the DB write.
- For error branch tests on existing services: use Moq to make the repository
  or DbContext throw, verify the exception propagates or is swallowed as specified.
- Coverlet exclude pattern for migrations:
  [Flowamaz.Infrastructure]Flowamaz.Infrastructure.Persistence.Migrations.*
