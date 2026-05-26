---
prompt-id: 03-05-copilot-sop-creation
phase: 03
sequence: 5
roles: [Executor, Verifier]
type: feature
depends-on: [03-04-visual-conversation-creation]
estimated-complexity: Medium
---

# Co-pilot Full Implementation + SOP/Document Upload

## Context
Co-pilot pattern matcher exists (prompt 03-03). Now wire the AI fallback for unknown
commands (Haiku for simple, Sonnet for complex) and build the SOP/document upload
creation method. See FUNCTIONAL.md §7 (Co-pilot) and §6.5 (document upload).

## Objective
Full Co-pilot with pattern match first, semantic cache second, then tiered AI fallback
(Haiku for structural changes, Sonnet for novel patterns). SOP/document upload pipeline.

## Scope

### What to Build

**Full Co-pilot Service (Flowamaz.Application/Workflow/Creation/CopilotService.cs):**

`ICopilotService` + `CopilotService`:

`ProcessCommandAsync(command, yamlContent, workspaceId, userId, ct)` → CopilotResult

Full pipeline (from FUNCTIONAL.md §7.2):
1. Rate check: IRateLimitService("copilot:{workspaceId}:{userId}", 60, 3600)
   → 429 if exceeded
2. Budget check: workspace_ai_budget — if capped → 429 with upgrade prompt
3. Pattern match: ICopilotPatternMatcher.TryMatch(command, graph)
   → if matched: return patch immediately, zero AI cost
4. Semantic cache: ISemanticCacheService.GetAsync(hash(F1 + workspaceId + command))
   → if hit: return cached patch
5. Context extraction: identify relevant subgraph (nodes adjacent to edit target)
   → send only the subgraph YAML (not full YAML) to reduce tokens
6. Model selection:
   - Simple structural change (add/remove/connect) → Haiku (F1)
   - Complex logic / new pattern → Sonnet (F1 with override? No — F1 always Haiku,
     but prompt complexity determines token usage)
7. Cached system prompt: FlowAmaz YAML spec + current subgraph + rules
   Apply cache_control: ephemeral to system prompt
8. User message: the command
9. Request JSON patch output format (not full YAML — reduces output tokens 80-90%)
10. Stream response token by token
11. Cache write: ISemanticCacheService.SetAsync(hash, patch, 24h)
12. Metering: IAiTokenMeteringService.RecordUsageAsync(F1, ...)

CopilotResult:
```csharp
record CopilotResult(
    bool Success,
    string? YamlPatch,            // JSON patch (RFC 6902)
    string? MatchedPattern,       // if pattern matched
    bool CacheHit,
    int? TokensUsed,
    string? ErrorMessage
);
```

**Update Co-pilot API endpoint (from stub in 03-03):**
Now wires full CopilotService including AI fallback.
Returns Server-Sent Events stream for the AI response.

**SOP/Document Upload Pipeline:**

`ISopParsingService` + `SopParsingService`:

`ParseAsync(fileBytes, mimeType, workspaceId, ct)` → SopParseResult

Supported formats: PDF, DOCX, TXT.
For PDF: extract text using iText or PdfPig (no OCR needed for Phase 3 — text PDFs only)
For DOCX: extract text using DocumentFormat.OpenXml
For TXT: read directly

Pipeline:
1. Extract text content from document
2. Truncate to 100k characters (sufficient for most SOPs, within Sonnet context)
3. Build parsing prompt:
   System: "Extract a business workflow from this Standard Operating Procedure document.
   Identify numbered steps, decision points, roles/approvers, and systems. Return JSON."
4. Call IAiCompletionService (F7 — document parsing function, Sonnet with long context)
5. Convert extracted JSON to NlWorkflowRequest
6. Call NlYamlGenerationService
7. Return: { YamlContent, ExtractedSteps, PageCount, WordCount, TokensUsed }

**API endpoint:**
`POST /api/v1/workspaces/{workspaceId}/workflows/from-document`
[RequireWorkspaceRole(Designer)]
Content-Type: multipart/form-data
Body: { document: file (max 20MB, pdf/docx/txt) }

**Frontend — Creation Method Selector:**

`CreationMethodSelector.vue`:
Now that all 6 methods are built, wire them all:
- Plain English (NL template) → NlTemplateForm.vue
- Voice Input → VoiceInputButton on NL form
- Whiteboard photo → VisualInputPanel.vue
- Conversation/Slack → ConversationImportPanel.vue
- Document/SOP → DocumentUploadPanel.vue (new)
- Canvas → directly opens SfgCanvas.vue (blank)

Shown as 6 cards on the "New Workflow" screen. Each card:
- Icon + label
- One-line description
- "Coming soon" badge → remove from all 6 (all now implemented)

`DocumentUploadPanel.vue`:
- File drop zone: PDF, DOCX, TXT up to 20MB
- Parsing progress: "Reading document... Extracting process steps... Generating workflow..."
- Shows extracted step count: "Found 12 process steps in 24 pages"
- Result: workflow preview before committing to canvas

**Unit tests:**
- CopilotService: pattern match → returns immediately, no AI call
- CopilotService: semantic cache hit → returns immediately, no AI call
- CopilotService: unknown command → calls AI, returns patch
- CopilotService: rate limit exceeded → returns 429 error result
- SopParsingService: DOCX text extraction → non-empty string
- SopParsingService: PDF extraction → non-empty string

## Technical Requirements
- [ ] Co-pilot: full pipeline in order (rate → budget → pattern → cache → AI)
- [ ] Co-pilot: context minimisation — only relevant subgraph sent to AI
- [ ] Co-pilot: JSON patch output format (not full YAML)
- [ ] Co-pilot: prompt caching on system prompt
- [ ] SOP: text extraction before AI call — no raw binary to AI
- [ ] SOP uses IAiCompletionService (F7) — not F2
- [ ] All 6 creation method cards active on "New Workflow" screen

## Acceptance Criteria
- [ ] Co-pilot: same command twice → second call returns cache_hit: true
- [ ] Co-pilot: 61st call in an hour → 429 response
- [ ] Co-pilot: "add timeout" pattern → matched_pattern set, no AI call
- [ ] SOP: upload a PDF → ExtractedSteps has step list
- [ ] "New Workflow" screen: all 6 cards active (no "coming soon")

## Output Expected
```
backend/Flowamaz.Application/Workflow/Creation/CopilotService.cs
backend/Flowamaz.Application/Workflow/Creation/SopParsingService.cs
backend/Flowamaz.Api/Controllers/WorkflowCreationController.cs (endpoints updated)
backend/Flowamaz.Tests.Unit/Workflow/Creation/CopilotServiceTests.cs
backend/Flowamaz.Tests.Unit/Workflow/Creation/SopParsingServiceTests.cs
web/src/components/creation/CreationMethodSelector.vue
web/src/components/creation/DocumentUploadPanel.vue
web/src/views/workflow/NewWorkflowView.vue (wires all 6 methods)
```
