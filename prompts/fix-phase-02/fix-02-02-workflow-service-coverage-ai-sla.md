---
prompt-id: fix-02-02-workflow-service-coverage-ai-sla
phase: fix-phase-02
sequence: 2
roles: [Executor, Verifier, Testing]
type: fix
source-issue: Testing Agent + Verifier — Phase 02 report
severity: High
depends-on: [fix-02-01-worker-jobs-coverage]
---

# Fix: Application/Workflow 73% Coverage + Wire AI + SLA Threshold Field

## Issues Being Fixed

**Issue 1 — Application/Workflow service coverage 73%:**
```
ISSUE: Application/Workflow services 73% — cover InstanceService read paths
+ orchestrator CompleteNode/Cancel/UpsertVariable
SEVERITY: High
FIX REQUIRED: Add tests for uncovered service paths.
```

**Issue 2 — AI completion is a stub:**
```
ISSUE: AI completion is a deterministic local stub, not a real provider call
FILE: backend/Flowamaz.Infrastructure/Services/AiCompletionService.cs
SEVERITY: Medium
FINDING: CEO narrative + PI insights resolve F5 and meter tokens but the
provider SDK call is not wired.
FIX REQUIRED: Wire Anthropic.SDK behind IAiCompletionService when a platform key is configured.
```

**Issue 3 — SLA threshold has no storage source:**
```
ISSUE: Per-workflow SLA threshold has no storage source
SEVERITY: Medium
FINDING: WorkflowMetric.SlaThresholdMs always null — SLA-risk insights never fire.
FIX REQUIRED: Add SLA setting to workflow definition / workspace settings.
```

## Objective
Raise Application/Workflow coverage to ≥ 80% by testing InstanceService read paths
and orchestrator methods. Wire real Anthropic.SDK call in AiCompletionService.
Add SlaThresholdMs field to WorkflowDefinition so SLA-risk insights can fire.

## Scope

### Fix 1 — Workflow Service Coverage Tests

**InstanceServiceTests (extend existing):**

Read path tests (currently uncovered):
- GetDetailAsync_ExistingInstance_ReturnsMaskedSensitiveVariables
  Create instance with one sensitive and one non-sensitive variable.
  Verify sensitive value = "***", non-sensitive = actual value.

- GetTimelineAsync_CompletedInstance_NodesOrderedByOffsetMs
  Instance with 3 completed nodes. Verify nodes sorted by offset_ms ascending.

- GetEventsAsync_LargeEventLog_ReturnsPaginated
  Insert 150 events. Request page 2 (size 50). Verify events 51-100 returned.

- GetVariablesAsync_SensitiveVariable_MaskedInResponse
  Sensitive=true variable → value shows "***".

- RetryAsync_FailedInstance_CreatesNewInstanceFromSameVersion
  Failed instance with version V1. Retry creates new instance pinned to V1.

- CancelAsync_CompletedInstance_Returns409
  Completed instances cannot be cancelled. Verify 409 Conflict.

- CancelAsync_RunningInstance_SetsStatusCancelledAndAppendsEvent
  Happy path — verify status=Cancelled, InstanceCancelled event in event log.

**WorkflowOrchestratorTests (extend existing):**

- CompleteNodeAsync_AllPredecessorsComplete_TriggersNextNode
  Graph: A→B→C. Complete A. Complete B. Verify C is queued for execution.

- CompleteNodeAsync_ParallelJoin_WaitsForAllBranches
  Graph: A → [B, C] → D. Complete A. Complete B. D not triggered (C pending).
  Complete C. D triggered.

- CancelAsync_RunningInstance_SetsStatusAndReleasesLease
  Verify instance.Status=Cancelled, lease released, InstanceCancelled event.

- UpsertVariableAsync_NewVariable_Created
  Verify new WorkflowVariable row created.

- UpsertVariableAsync_ExistingVariable_Updated
  Verify existing row updated (not duplicated).

- UpsertVariableAsync_SensitiveVariable_StoredEncrypted
  (Stub for now — sensitive=true flag set, encryption Phase 4)

**WorkflowInterpreterServiceTests (extend existing):**
- GenerateNarrativeAsync_AudienceDeveloper_ContainsNodeDurationsAndPayloads
- GenerateNarrativeAsync_AudienceAuditor_ContainsAllTimestampsAndActors
- GenerateNarrativeAsync_AudienceCeo_CallsAiCompletionService

### Fix 2 — Wire Real Anthropic.SDK in AiCompletionService

**AiCompletionService changes:**

The current stub returns a deterministic local response. Wire real calls:

```csharp
public async Task<string> CompleteAsync(
    string functionId, string systemPrompt, string userPrompt,
    Guid workspaceId, CancellationToken ct)
{
    var config = await _modelResolution.ResolveModelConfigAsync(functionId, workspaceId, ct);

    // If no platform key configured → fall back to stub (development/test mode)
    if (string.IsNullOrEmpty(config.ApiKey))
    {
        _logger.LogWarning("AiCompletionService: no API key for {Provider}/{Model} — using stub",
            config.Provider, config.ModelId);
        return GenerateStubResponse(functionId, userPrompt);
    }

    // Real Anthropic SDK call
    var client = new AnthropicClient(config.ApiKey);
    var response = await client.Messages.CreateAsync(new MessageCreateParams
    {
        Model = config.ProviderModelId,  // uses ProviderModelId not internal ModelId
        MaxTokens = 1024,
        System = systemPrompt,
        Messages = [new() { Role = "user", Content = userPrompt }]
    }, ct);

    var content = response.Content.OfType<TextBlock>().FirstOrDefault()?.Text ?? "";

    // Meter usage
    await _metering.RecordUsageAsync(
        functionId, config.ModelId, config.Provider, workspaceId,
        response.Usage.InputTokens, response.Usage.OutputTokens,
        CalculateCost(config.ModelId, response.Usage), ct);

    return content;
}
```

Unit test for the wired service:
- CompleteAsync_PlatformKeyConfigured_CallsAnthropicSdk (mock AnthropicClient)
- CompleteAsync_NoPlatformKey_UsesStubAndLogsWarning
- CompleteAsync_SdkThrows_PropagatesException (caller handles)

### Fix 3 — SLA Threshold Field

**Add SlaThresholdMs to WorkflowDefinition:**
```csharp
// WorkflowDefinition entity
public long? SlaThresholdMs { get; set; }  // null = no SLA defined
```

Migration: `AddWorkflowSlaThreshold`
```sql
ALTER TABLE workflow_definitions ADD COLUMN sla_threshold_ms bigint;
```

**API changes:**
- Include sla_threshold_ms in CreateWorkflowDefinitionRequest and UpdateWorkflowDefinitionRequest
- Return it in WorkflowDefinitionDto
- Validate: if set, must be > 0

**ProcessIntelligenceService change:**
Replace hardcoded null SLA with:
```csharp
var slaThresholdMs = workflowDef.SlaThresholdMs;
if (slaThresholdMs.HasValue && avgDurationMs > slaThresholdMs.Value * 0.8)
{
    // create SlaRisk insight
}
```

**Unit test:**
- ProcessIntelligenceService_SlaThresholdSet_TriggersRiskInsightAt80Pct
- ProcessIntelligenceService_NoSlaThreshold_NoRiskInsight

## Technical Requirements
- [ ] All new tests use mocked dependencies — no real external calls
- [ ] AiCompletionService: real SDK call only when ApiKey is non-empty
- [ ] AiCompletionService: stub path logs Warning (not silent)
- [ ] SlaThresholdMs migration applies cleanly on existing schema
- [ ] ProcessIntelligenceService: SLA risk fires when avg > threshold * 0.8
- [ ] dotnet build — 0 errors, 0 warnings
- [ ] dotnet test — all pass
- [ ] Application/Workflow coverage ≥ 80% (Coverlet confirmed)

## Acceptance Criteria
- [ ] InstanceService: sensitive variable → "***" in GetDetailAsync (test proves)
- [ ] Orchestrator: parallel join waits for all branches (test proves)
- [ ] AiCompletionService: with platform key → AnthropicClient called (test proves)
- [ ] AiCompletionService: without platform key → stub + Warning log (test proves)
- [ ] WorkflowDefinition: sla_threshold_ms field in API request/response
- [ ] SLA risk insight created when avg_duration_ms > sla_threshold_ms * 0.8 (test proves)
- [ ] Application/Workflow coverage ≥ 80% confirmed by Coverlet

## Output Expected
```
backend/Flowamaz.Core/Entities/Workflow/WorkflowDefinition.cs (SlaThresholdMs added)
backend/Flowamaz.Infrastructure/Persistence/Migrations/[ts]_AddWorkflowSlaThreshold.cs
backend/Flowamaz.Infrastructure/Services/AiCompletionService.cs (real SDK wired)
backend/Flowamaz.Application/Workflow/DTOs/ (SlaThresholdMs in request/response DTOs)
backend/Flowamaz.Tests.Unit/Workflow/InstanceServiceTests.cs (extended)
backend/Flowamaz.Tests.Unit/Workflow/WorkflowOrchestratorTests.cs (extended)
backend/Flowamaz.Tests.Unit/Workflow/WorkflowInterpreterServiceTests.cs (extended)
backend/Flowamaz.Tests.Unit/Ai/AiCompletionServiceTests.cs
backend/Flowamaz.Tests.Unit/Analytics/ProcessIntelligenceServiceTests.cs (extended)
```
