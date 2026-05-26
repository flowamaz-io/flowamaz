---
prompt-id: 04-03-ai-node-worker
phase: 04
sequence: 3
roles: [Executor, Verifier, Security]
type: feature
depends-on: [04-02-official-connectors]
estimated-complexity: High
---

# AI Node Worker — Multi-Model, BYOM, Fallback Chain, Output Schema Validation

## Context
Connectors exist. Now build the full AI node execution worker — the runtime layer
that executes AI nodes within workflows. Phase 2 had a stub. This is the production
implementation. See FUNCTIONAL.md §5 (AI governance) and §2 (architecture).

## Objective
Full AI node worker: multi-model resolution per node, BYOM endpoint support,
fallback chain on failure, output schema validation with auto-retry, sensitive
variable stripping, and per-node cost tracking.

## Scope

### What to Build

**AI Node Worker (Flowamaz.Application/Workflow/Workers/AiNodeWorker.cs):**

Implements `INodeWorker` (SupportedType = Ai).

`ExecuteAsync(context, ct)` → NodeExecutionResult

Full execution pipeline:

1. **Model resolution** (5-level hierarchy from FUNCTIONAL.md §5.3):
   Read node.Config for model override → workflow default_model →
   call IModelResolutionService.ResolveModelConfigAsync(F4, workspaceId)

2. **Sensitive variable stripping**:
   Read all variables from context. For any WorkflowVariable where IsSensitive=true:
   replace value with "[SENSITIVE_REDACTED]" before including in AI prompt.
   Sensitive values NEVER sent to external AI providers.

3. **Input construction**:
   Build messages from node.Config.prompt_template (supports {{ variable.name }} interpolation)
   Apply IVariableEvaluationService for template substitution

4. **Fallback chain**:
   If node.Config has fallback_models: [model1, model2]:
   Try primary model → on failure (API error, timeout): try fallback model(s) in order
   Log each attempt with model name and failure reason

5. **AI provider call** via IAiCompletionService (F4)

6. **Output schema validation**:
   If node.Config.output_schema defined:
   - Validate AI response against JSON schema (NJsonSchema)
   - If fails: construct self-correction prompt with schema + error → retry ONCE
   - If second attempt fails: return NodeExecutionResult { Success=false, ShouldRetry=false }

7. **Response parsing**:
   If output_schema: parse validated JSON response into structured output
   If no schema: raw text response stored as output variable

8. **Cost tracking**:
   IAiTokenMeteringService.RecordUsageAsync(F4, modelId, provider, workspaceId, ...)
   ALWAYS called even if execution failed (partial usage counts)

9. **Result**:
   NodeExecutionResult with structured output stored as WorkflowVariable

**BYOM Support (Bring Your Own Model):**

For Enterprise workspaces with custom model endpoints:
If resolved config has Provider = "byom":
- Retrieve BYOM endpoint URL from WorkspaceCredential (credentialId in AiOptions)
- Build OpenAI-compatible request (BYOM endpoints follow OpenAI API shape)
- Send via HttpClientFactory with BYOM credential in Authorization header
- Handle response as OpenAI ChatCompletion response

BYOM credential stored in CredentialVaultService (type: byom-endpoint).

**Sensitive Variable Stripping — enforcement:**

Rule: AI providers are external services. Sensitive workflow variables MUST NEVER
be sent to any AI provider (even platform-managed Anthropic).

Implementation:
- Before ANY AI call in the worker, scan all variables being included
- Variables marked IsSensitive=true → replace with "[SENSITIVE_REDACTED]"
- Log at Warning level: "Stripped {count} sensitive variable(s) before AI call on node {nodeId}"
- This applies to BYOM providers too — no exception

**AI Node YAML configuration:**

```yaml
- id: classify-risk
  type: ai
  label: "Classify Document Risk"
  config:
    model: claude-sonnet-4-6    # optional override (F4 workspace default used if omitted)
    fallback_models:
      - claude-haiku-4-5
      - gpt-4o-mini
    prompt_template: |
      Classify the risk level of this document:
      Title: {{ variables.document_title }}
      Content: {{ variables.document_content }}
      Return JSON: { "risk": "low|medium|high", "reason": "string" }
    output_schema:
      type: object
      required: [risk, reason]
      properties:
        risk:
          type: string
          enum: [low, medium, high]
        reason:
          type: string
    max_tokens: 500
    temperature: 0
    cost_cap_usd: 0.05    # per-call hard cap — fail if estimated cost exceeds
```

**Per-call cost cap:**
If node.Config.cost_cap_usd set:
- Estimate cost before call (input_tokens * input_rate + max_tokens * output_rate)
- If estimated > cost_cap_usd → fail with error "AI call would exceed per-node cost cap"
- This prevents runaway costs on nodes processing large documents

**Unit tests:**
- AiNodeWorker: sensitive variable → stripped before AI call (mock verifies no sensitive value in prompt)
- AiNodeWorker: fallback chain → primary fails → fallback model used
- AiNodeWorker: output schema validation fail → self-correction retry (mock)
- AiNodeWorker: second attempt fails → NodeExecutionResult.Success=false
- AiNodeWorker: cost cap exceeded → fails before API call
- AiNodeWorker: BYOM provider → OpenAI-compatible request shape

## Technical Requirements
- [ ] Sensitive variables: stripped BEFORE any AI provider call, no exceptions
- [ ] Fallback chain: each attempt logged with model + failure reason
- [ ] Output schema: validates via NJsonSchema, self-correction retry max 1
- [ ] BYOM: OpenAI-compatible endpoint, credential from vault
- [ ] Cost cap: estimated before call using known token rates
- [ ] Metering: RecordUsageAsync called even on failed calls (partial usage)

## Acceptance Criteria
- [ ] Sensitive variable test: mock proves no sensitive value in prompt string
- [ ] Fallback test: primary 500 → fallback model used (proven by mock)
- [ ] Schema validation: invalid JSON → self-correction message sent (proven by mock)
- [ ] Cost cap: estimated > cap → exception before any HTTP call
- [ ] All tests pass

## Output Expected
```
backend/Flowamaz.Application/Workflow/Workers/AiNodeWorker.cs
backend/Flowamaz.Application/Connectors/Services/ByomProviderService.cs
backend/Flowamaz.Tests.Unit/Workflow/Workers/AiNodeWorkerTests.cs
```
