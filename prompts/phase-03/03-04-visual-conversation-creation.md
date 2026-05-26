---
prompt-id: 03-04-visual-conversation-creation
phase: 03
sequence: 4
roles: [Executor, Verifier, UX]
type: feature
depends-on: [03-03-nl-yaml-creation]
estimated-complexity: High
---

# Visual Input (Whiteboard→Workflow) + Conversation→Workflow

## Context
NL→YAML pipeline exists. Now build the two most "magic" creation methods:
Visual input (whiteboard/paper/diagram photo → workflow) using Claude vision (F3),
and Conversation→Workflow (Slack/Teams/email thread → workflow).
See FUNCTIONAL.md §6.3 and §6.4.

## Objective
Image upload pipeline that extracts workflow structure from photos using Claude Sonnet vision.
Conversation import that extracts process steps from Slack/Teams/email threads.
Both feed into the existing YAML validator + canvas.

## Scope

### What to Build

**Visual Input Pipeline:**

`IVisualInputService` + `VisualInputService`:

`ProcessImageAsync(imageBytes, mimeType, workspaceId, ct)` → VisualInputResult

Pipeline:
1. Client-side pre-processing (resize to max 1920px, convert to JPEG) — done in Vue before upload
2. Encode image as base64
3. Build vision prompt:
   System: "You are a workflow diagram parser. Extract all process elements.
   Return JSON only. For each element: {id, label, type, connections, annotations, confidence_score}"
   User: [image content block] + "Parse this workflow diagram. Return the JSON structure."
4. Call IAiCompletionService (F3 — Sonnet with vision) — NOT F2 (different function)
5. Parse JSON response into VisualParseResult
6. Connector matching: label text → known connector ID (BambooHR→bamboohr, SAP→sap-s4hana, etc.)
7. Filter low-confidence elements (< 0.80) → collect for user confirmation
8. Generate YAML from high-confidence elements
9. Return: { YamlDraft, LowConfidenceElements, WorkflowGraph, EstimatedCost }

VisualParseResult (from Claude):
```json
{
  "nodes": [
    { "id": "n1", "label": "Submit Request", "type": "trigger",
      "x": 120, "y": 80, "confidence": 0.98,
      "annotations": ["Typeform", "webhook"] },
    { "id": "n2", "label": "Amount > $5000?", "type": "router",
      "confidence": 0.94, "conditions": ["amount > 5000", "amount <= 5000"] }
  ],
  "edges": [
    { "from": "n1", "to": "n2", "label": null }
  ],
  "low_confidence": ["n4", "n7"]
}
```

Cost per visual upload: ~$0.01-0.02 (image tokens + text). Metered via IAiTokenMeteringService.

**API endpoint:**
`POST /api/v1/workspaces/{workspaceId}/workflows/from-image`
[RequireWorkspaceRole(Designer)]
Content-Type: multipart/form-data
Body: { image: file (max 10MB, jpg/png/pdf/heic) }
Returns: VisualInputResult { yaml_draft, low_confidence_elements[], cost_usd }

**Frontend — Visual Input UI:**

`VisualInputView.vue` (or modal):
- Large drop zone: "Upload a whiteboard, sketch, or diagram photo"
- Camera icon for mobile (accepts from camera roll)
- File size limit shown: max 10MB
- Client-side resize using Canvas API before upload
- Upload progress indicator
- After processing: split view
  Left: original image
  Right: detected elements list with confidence scores, type badges
- Low-confidence review: for each element below 80%:
  "This looks like a human gate — is that right? [Yes] [Change to: Action / Router / End]"
- After confirmation: canvas renders with workflow
- "Original image stored as reference" — shown in workflow detail sidebar

**Conversation→Workflow Pipeline:**

`IConversationImportService` + `ConversationImportService`:

`ImportAsync(conversationText, sourceType, workspaceId, ct)` → ConversationImportResult

Input types: plain text paste, Slack export JSON (message array), Teams transcript.
Source types: Slack, Teams, Email, Generic.

Pipeline:
1. Parse input by sourceType (extract messages with author + content)
2. Build extraction prompt:
   System: "Extract a business workflow from this conversation. Identify: process steps,
   decision points, approvers, systems mentioned, conditions. Return structured JSON."
   User: [conversation text]
3. Call IAiCompletionService (F2 — Sonnet)
4. Parse extracted process into NlWorkflowRequest (reuse existing NL pipeline)
5. Call NlYamlGenerationService with extracted request
6. Return: { YamlContent, ExtractedProcess, ConfidenceScore, TokensUsed }

ExtractedProcess shown to user for review before YAML is committed:
"I found a purchase approval process. Anita submits, Rajan approves up to $5,000,
CFO approves above. SAP is mentioned for PO creation. Does this look right?"
[Yes, generate workflow] [Edit before generating]

**API endpoint:**
`POST /api/v1/workspaces/{workspaceId}/workflows/from-conversation`
[RequireWorkspaceRole(Designer)]
Body: { text: string, source_type: "slack"|"teams"|"email"|"generic" }
Returns: ConversationImportResult { yaml_content, extracted_process, confidence_score }

**Frontend — Conversation Import UI:**

`ConversationImportView.vue`:
- Textarea: "Paste your Slack thread, email chain, or meeting notes"
- Source type selector: [Slack] [Teams] [Email] [General text]
- Slack: show example format hint
- Submit → shows ExtractedProcess preview with edit option
- Confirm → canvas populates

**Unit tests:**
- VisualInputService: mock Claude response → correct node types extracted
- VisualInputService: low confidence node → appears in low_confidence_elements
- VisualInputService: connector label "SAP" → matched to sap-s4hana connector ID
- ConversationImportService: Slack JSON parsed into messages correctly
- ConversationImportService: extracted process contains approver names from conversation

## Technical Requirements
- [ ] Visual input uses IAiCompletionService (F3) — not F2 (different model resolution)
- [ ] Tokens metered for F3 calls via IAiTokenMeteringService
- [ ] Image pre-processing done client-side — max 1920px before upload
- [ ] Max upload size: 10MB enforced at nginx and API
- [ ] Conversation import uses F2 for extraction — tokens metered
- [ ] Low-confidence review: max 5 confirmation questions per image
- [ ] VisualInputService: gracefully handles malformed Claude JSON response

## Acceptance Criteria
- [ ] POST /from-image with whiteboard photo → valid YAML draft returned
- [ ] POST /from-image: element with confidence < 0.80 → in low_confidence_elements
- [ ] POST /from-conversation with Slack thread mentioning "manager approval" → human-gate node in output
- [ ] Frontend: image drop zone works, progress shown, result rendered on canvas
- [ ] Frontend: conversation import textarea → extracted process preview shown

## Output Expected
```
backend/Flowamaz.Application/Workflow/Creation/VisualInputService.cs
backend/Flowamaz.Application/Workflow/Creation/ConversationImportService.cs
backend/Flowamaz.Api/Controllers/WorkflowCreationController.cs (endpoints added)
backend/Flowamaz.Tests.Unit/Workflow/Creation/VisualInputServiceTests.cs
backend/Flowamaz.Tests.Unit/Workflow/Creation/ConversationImportServiceTests.cs
web/src/components/creation/VisualInputPanel.vue
web/src/components/creation/ConversationImportPanel.vue
web/src/components/creation/VisualConfirmationStep.vue
```
