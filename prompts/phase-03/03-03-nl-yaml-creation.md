---
prompt-id: 03-03-nl-yaml-creation
phase: 03
sequence: 3
roles: [Executor, Verifier, UX]
type: feature
depends-on: [03-02-sfg-canvas]
estimated-complexity: High
---

# NL→YAML Generation + Voice Input + Co-pilot Foundation

## Context
Canvas and validator exist. Now build the NL→YAML generation pipeline — the feature
that defines Flowamaz. Uses IAiCompletionService (F2 — now wired with real Anthropic.SDK).
Also build voice input and the Co-pilot command processor foundation.

## Objective
NL template form → Claude Sonnet → validated YAML → canvas. Voice input via Web Speech API.
Co-pilot pattern matcher (zero AI cost for common commands). Full cost control applied.

## Scope

### What to Build

**NL→YAML Generation Service (Flowamaz.Application/Workflow/Creation/):**

`INlYamlGenerationService` + `NlYamlGenerationService`:

`GenerateAsync(request, workspaceId, ct)` → GenerationResult

Input — NlWorkflowRequest (6-section template):
```csharp
record NlWorkflowRequest(
    string WorkflowName,
    string Purpose,          // what it does, who uses it
    string TriggerDescription,   // what starts it, input fields
    string StepsDescription,     // each step, decisions, branches
    string RulesAndConstraints,  // thresholds, SLA, failure handling
    string SystemsAndAi,         // connectors needed, AI preference
    string? ExistingContext      // optional: prior conversation extract
);
```

Generation pipeline:
1. Build system prompt (YAML spec + examples + rules — use prompt caching)
2. Build user prompt from 6-section request
3. Call IAiCompletionService (F2 — Sonnet) with streaming
4. Parse response as YAML
5. Run 6-layer validator on generated YAML
6. If validation fails: attempt self-correction (one retry with errors as context)
7. Return: { YamlContent, WorkflowGraph, ValidationResult, TokensUsed }

System prompt includes:
- Complete workflow-v1.schema.json
- 3 example YAML files (purchase approval, employee onboarding, incident response)
- Rules: "Generate only the YAML. No explanation. Use only declared variable names."
- "If you cannot model a step precisely, add an annotation node with a note."

Cost control applied:
- System prompt cached (prompt caching via cache_control: ephemeral)
- Max tokens: 4096 output (sufficient for complex workflows)
- Temperature: 0 (deterministic generation)
- Semantic cache: hash(6-section request) → cached YAML if same input seen before

**API endpoint:**
`POST /api/v1/workspaces/{workspaceId}/workflows/generate`
[RequireWorkspaceRole(Designer)]
Body: NlWorkflowRequest
Returns: stream (SSE) — tokens stream as generated
Final message: { yaml_content, validation_result, tokens_used, cached: bool }

**Voice Input (web/src/composables/useVoiceInput.ts):**

Uses Web Speech API (SpeechRecognition).
- startListening() — begins recording
- stopListening() — ends, returns transcript
- isListening, transcript, error (reactive)
- Language: detect from browser locale, configurable
- Works in Chrome/Edge/Safari (not Firefox — show browser warning)

Voice input feeds into the same NL template form. Microphone icon on each section field.
User speaks → transcript populates the field → user reviews and submits.

**Co-pilot Pattern Matcher (zero AI cost for 80% of commands):**

`ICopilotPatternMatcher` + `CopilotPatternMatcher`:

`TryMatch(command, workflowGraph)` → PatternMatchResult?

20 patterns using regex + intent detection:

```
Pattern: add-timeout
Regex: /add.*timeout.*(\d+)\s*(h|hour|m|min|s|sec)/i
Input: "Add 48h timeout to manager-gate"
Output: YAML patch { nodes[id=manager-gate].timeout: { seconds: 172800 } }

Pattern: add-retry
Regex: /retry.*(\d+).*time/i
Input: "Retry the SAP step 3 times"
Output: YAML patch { nodes[id=sap-step].retry: { max_attempts: 3 } }

Pattern: connect-nodes
Regex: /connect\s+(\S+)\s+to\s+(\S+)/i
Input: "Connect validate-employee to amount-router"
Output: YAML patch { edges: [{ from: validate-employee, to: amount-router }] }

Pattern: add-human-gate
Regex: /add.*(approval|gate|review).*after\s+(\S+)/i
Input: "Add manager approval after validate-employee"
Output: YAML patch with new human-gate node + edges

Pattern: set-sla
Regex: /sla.*(\d+)\s*(h|hour|day)/i
Input: "Set SLA to 48 hours"
Output: YAML patch { spec.sla_threshold_ms: 172800000 }

Pattern: add-error-handler
Regex: /add.*(error|failure).*handler/i
Input: "Add error handler that emails ops team"
Output: YAML patch with try-catch wrapper + email action node
```

(implement all 20 patterns from FUNCTIONAL.md §7.3)

**Co-pilot API endpoint (preview — full implementation Phase 3 prompt 04):**
`POST /api/v1/workspaces/{workspaceId}/workflows/{id}/copilot`
Body: { command: string, yaml_content: string }
[RequireWorkspaceRole(Designer)]
Returns: { matched_pattern: string?, yaml_patch: string?, cache_hit: bool }
For Phase 3: pattern matcher only — AI fallback comes in prompt 04.

**Rate limiting applied from day 1:**
IRateLimitService.CheckAndIncrementAsync("copilot:{workspaceId}:{userId}", 60, 3600)
Returns 429 if limit exceeded.

**Unit tests:**
- NlYamlGenerationService: valid 6-section input → ValidYaml generated (mock AI)
- NlYamlGenerationService: invalid YAML from AI → retry with error context (mock)
- NlYamlGenerationService: semantic cache hit → no AI call
- CopilotPatternMatcher: all 20 patterns match expected inputs
- CopilotPatternMatcher: unknown command → null (no match)

## Technical Requirements
- [ ] NL→YAML uses IAiCompletionService (F2) — not hardcoded model
- [ ] System prompt uses prompt caching (cache_control: ephemeral)
- [ ] Semantic cache checked before every AI call
- [ ] One retry on validation failure — no infinite loop
- [ ] Voice input: graceful degradation if browser does not support SpeechRecognition
- [ ] Pattern matcher returns null (not throws) on no match
- [ ] Rate limiting: 429 after 60 Co-pilot calls/user/hour

## Acceptance Criteria
- [ ] POST /generate with valid 6-section request → valid YAML in response
- [ ] POST /generate same request twice → second returns cached: true, no AI call
- [ ] Pattern "add 48h timeout to manager-gate" → yaml_patch with timeout.seconds=172800
- [ ] Pattern "connect A to B" → yaml_patch with new edge
- [ ] Unknown command → matched_pattern: null
- [ ] Voice input: microphone icon on NL form, transcript populates field

## Output Expected
```
backend/Flowamaz.Application/Workflow/Creation/NlYamlGenerationService.cs
backend/Flowamaz.Application/Workflow/Creation/CopilotPatternMatcher.cs
backend/Flowamaz.Api/Controllers/WorkflowCreationController.cs
backend/Flowamaz.Tests.Unit/Workflow/Creation/NlYamlGenerationServiceTests.cs
backend/Flowamaz.Tests.Unit/Workflow/Creation/CopilotPatternMatcherTests.cs
web/src/composables/useVoiceInput.ts
web/src/components/creation/NlTemplateForm.vue
web/src/components/creation/VoiceInputButton.vue
web/src/services/creation.service.ts
```
