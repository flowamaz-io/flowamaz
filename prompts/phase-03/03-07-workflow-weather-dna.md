---
prompt-id: 03-07-workflow-weather-dna
phase: 03
sequence: 7
roles: [Executor, Verifier, UX]
type: feature
depends-on: [03-06-workflow-editor-view]
estimated-complexity: Medium
---

# Workflow Weather (Full) + Workflow DNA

## Context
Workflow Weather was built in Phase 2 as a backend + basic widget. Now make it a
full executive dashboard screen. Also build Workflow DNA — similarity scoring and
clone suggestions. See FUNCTIONAL.md §8.7 (Weather) and §8.8 (DNA).

## Objective
Full Workflow Weather executive view with drill-down. Workflow DNA similarity scoring
that suggests cloning when creating a new workflow similar to an existing one.

## Scope

### What to Build

**Workflow Weather Full View (/weather):**

Full-screen executive view (not just a widget).
Real-time map of all workspace workflows organised by team/department.

Layout:
- Top bar: "Workflow Weather" title, last updated timestamp, refresh button
- Summary strip: 4 counts (green/yellow/orange/red workflow counts)
- Main area: workflow cards grid (responsive, 3-4 per row)
- Each card shows:
  - Workflow name (bold)
  - Status dot (large, coloured)
  - Active instances count
  - Runs today / runs this month
  - Last failure ago ("2h ago" or "None today")
  - SLA compliance % (if threshold set)
  - Pending insights count (amber bell icon if > 0)
- Card hover: expand to show latest insight message
- Card click: navigate to workflow detail

Status colours (from server, not client-calculated):
- Green: healthy → teal
- Yellow: warning → amber
- Orange: degraded → orange
- Red: critical → red (subtle pulsing animation)

Insights sidebar (slides in from right when insight bell clicked):
- Lists all unacknowledged insights for workspace
- Severity badge (Info/Warning/Critical)
- "Acknowledge" button per insight
- Group by workflow

**Auto-refresh:** Weather endpoint polled every 60 seconds.
"Last updated: 2m ago" shown in top bar.

**Sidebar navigation:** Add "Weather" item to sidebar below "Workflows".
Icon: cloud-sun (Lucide).

**Workflow DNA Service (Flowamaz.Application/Workflow/Dna/):**

`IWorkflowDnaService` + `WorkflowDnaService`:

`ComputeDnaAsync(workflowDefinitionId, ct)` → WorkflowDna

DNA fingerprint structure:
```csharp
record WorkflowDna(
    Guid WorkflowDefinitionId,
    string TriggerType,                    // "webhook", "form", etc.
    IReadOnlyList<string> NodeTypes,       // sorted: ["action","human-gate","router"]
    IReadOnlyList<string> ConnectorIds,    // connectors referenced
    bool HasAiNodes,
    bool HasHumanGates,
    bool HasParallelBranches,
    int NodeCount,
    int EdgeCount,
    long? SlaThresholdMs,
    decimal SlaComplianceRate,             // from metrics
    string DnaHash                         // SHA256 of above fields
);
```

`FindSimilarAsync(workspaceid, dna, limit, ct)` → List<WorkflowSimilarity>

Similarity score (0-100):
- +20 if same trigger type
- +15 if same HasHumanGates
- +15 if same HasAiNodes
- +10 if same HasParallelBranches
- +20 × (shared connector count / max connector count)
- +20 × (shared node types / total unique node types)

WorkflowSimilarity: { WorkflowDefinitionId, Name, SimilarityScore, SharedConnectors }

`SuggestCloneAsync(workspaceId, partialRequest, ct)` → CloneSuggestion?
When user starts typing a new workflow name or description, check DNA similarity.
If a workflow with score > 75 exists → suggest: "This is 87% similar to {name} — clone it?"

**API endpoints:**
`GET /api/v1/workspaces/{workspaceId}/workflows/{id}/dna` [Viewer]
`GET /api/v1/workspaces/{workspaceId}/workflows/{id}/similar` [Viewer]
  → list of similar workflows with scores

**Clone suggestion integration in New Workflow screen:**
When user types workflow name in CreationMethodSelector:
- Debounced 600ms → call /workflows?search={name} + compute similarity
- If similar workflow found (score > 75): show suggestion card:
  "This looks similar to {existing_name} (87% match). Clone and customise instead?"
  [Clone it] [Start fresh]
- Clone action: POST /workflows/{id}/clone → creates new draft with same YAML

`POST /api/v1/workspaces/{workspaceId}/workflows/{id}/clone` [Designer]:
- Creates new WorkflowDefinition with same YamlContent
- Name: "{original_name} (copy)"
- Status: Draft
- Records CreatedByMethod: Canvas (user will customise)

**Unit tests:**
- WorkflowDnaService: same trigger + same connectors → high similarity score
- WorkflowDnaService: completely different workflows → score < 20
- WorkflowDnaService: DnaHash is deterministic for same workflow

## Technical Requirements
- [ ] Weather view: auto-refreshes every 60 seconds
- [ ] Weather view: red cards have pulsing animation (Tailwind animate-pulse)
- [ ] DNA similarity: score 0-100, deterministic
- [ ] Clone suggestion: appears in < 1s after typing (debounced 600ms)
- [ ] Clone endpoint: new workflow in Draft status, not Published
- [ ] Insights sidebar: acknowledge updates WorkflowInsight.IsAcknowledged

## Acceptance Criteria
- [ ] /weather route: shows all workspace workflows as coloured cards
- [ ] Red workflow card: pulsing animation visible
- [ ] Click insight bell → sidebar opens with insights list
- [ ] DNA: two identical workflows → similarity score = 100
- [ ] Clone suggestion appears when typing name similar to existing workflow
- [ ] POST /clone → new workflow draft with same YAML in workspace

## Output Expected
```
backend/Flowamaz.Application/Workflow/Dna/WorkflowDnaService.cs
backend/Flowamaz.Api/Controllers/WorkflowDnaController.cs
backend/Flowamaz.Tests.Unit/Workflow/Dna/WorkflowDnaServiceTests.cs
web/src/views/weather/WorkflowWeatherView.vue
web/src/components/weather/WeatherInsightsSidebar.vue
web/src/components/workflow/CloneSuggestion.vue
web/src/views/workflow/NewWorkflowView.vue (clone suggestion wired)
```
