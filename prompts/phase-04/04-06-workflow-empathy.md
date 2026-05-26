---
prompt-id: 04-06-workflow-empathy
phase: 04
sequence: 6
roles: [Executor, Verifier, UX]
type: feature
depends-on: [04-05-connector-frontend]
estimated-complexity: Medium
---

# Workflow Empathy View

## Context
Instance execution data exists from Phase 2. Human gate data exists from this phase.
Now build Workflow Empathy — the second magic view on the workflow canvas showing
the human experience inside the workflow. See FUNCTIONAL.md §8.2.

## Objective
Add "Empathy View" as an alternate canvas mode showing the human experience layer:
email count, wait periods, status visibility gaps, days to outcome, friction points.

## Scope

### What to Build

**WorkflowEmpathyService (Flowamaz.Application/Workflow/Empathy/):**

`IWorkflowEmpathyService` + `WorkflowEmpathyService`:

`AnalyseAsync(workflowDefinitionId, workspaceId, ct)` → EmpathyAnalysis

Analyses the workflow from the perspective of the person who submitted it:

```csharp
record EmpathyAnalysis(
    Guid WorkflowDefinitionId,
    int EmailsSentToRequester,     // count of email/notification nodes visible to submitter
    int WaitPeriodCount,           // number of human gates the requester must wait through
    int StatusUpdateCount,         // times the requester receives a status notification
    double AvgDaysToOutcome,       // from historical run data (WorkflowMetric)
    int VisibilityGapHours,        // max hours without any status update to submitter
    IReadOnlyList<EmpathyIssue> Issues
);

record EmpathyIssue(
    string NodeId,
    string Label,
    EmpathyIssueType Type,         // NoStatusUpdate, LongWait, MultipleEmails, NoOutcomeNotification
    string Description,            // human-readable, from user perspective
    string Suggestion              // how to fix it
);
```

Detection logic:
- Walk the SFG graph
- Track: which nodes send notifications to the requester (email/Slack where assignee_variable = submitter)
- Count gates the requester waits through (HumanGate nodes in the critical path)
- Find gaps: > 24h between notifications to requester → flag as VisibilityGap
- No End node with notification → flag: requester never learns outcome
- > 3 notification nodes → flag: possibly too many emails

Example issues:
- "Your onboarding workflow sends 7 emails in 3 days to the new hire — consider consolidating"
- "New hire has zero status updates for 4 days between step 2 and step 5"
- "The new hire never receives confirmation that onboarding is complete — add an End notification"

**API endpoint:**
`GET /api/v1/workspaces/{workspaceId}/workflows/{id}/empathy`
[RequireWorkspaceRole(Designer)]
Returns: EmpathyAnalysis

**Frontend — Empathy View toggle:**

In WorkflowEditorView toolbar: add view toggle:
[Technical] [Empathy] — two-state button group.

In Empathy mode:
- Canvas overlay: nodes that cause submitter pain highlighted in red
- Nodes that update submitter shown in green
- Gap between last update and next update shown as "4-day silence" annotation
- Right panel: EmpathyPanel.vue (replaces node inspector in empathy mode)

`EmpathyPanel.vue`:
- Summary metrics: {N} emails, {N} wait periods, {N} status updates, avg {N} days to outcome
- Issues list: each issue with severity (High/Medium/Low), node highlight, suggestion
- "How to fix" expandable per issue
- Score: 0-100 (100 = perfect submitter experience)

Score calculation:
- Start at 100
- -10 per VisibilityGap > 24h
- -15 if NoOutcomeNotification
- -5 per excess email (above 3)
- -10 per unnecessary wait (gate that could be async)

**Help article:**
`web/src/help/articles/workflow-empathy.md`
"What is Workflow Empathy?" — explains the concept, why it matters, how to use it.

**Unit tests:**
- WorkflowEmpathyService: workflow with no outcome notification → NoOutcomeNotification issue
- WorkflowEmpathyService: workflow with 24h gap → VisibilityGap issue
- WorkflowEmpathyService: workflow with 7 emails → MultipleEmails issue
- WorkflowEmpathyService: well-designed workflow → no issues, high score

## Technical Requirements
- [ ] Empathy analysis is deterministic (no AI) — pure graph + historical analysis
- [ ] Toggle between Technical and Empathy view without data reload
- [ ] Empathy canvas overlay: only highlights relevant nodes
- [ ] Score: 0-100, consistent formula

## Acceptance Criteria
- [ ] /workflows/{id}/empathy → returns EmpathyAnalysis with issues
- [ ] Workflow with no outcome notification → NoOutcomeNotification issue in response
- [ ] Editor: [Empathy] toggle → canvas shows overlay, right panel shows EmpathyPanel
- [ ] EmpathyPanel: score + issues list rendered correctly

## Output Expected
```
backend/Flowamaz.Application/Workflow/Empathy/WorkflowEmpathyService.cs
backend/Flowamaz.Api/Controllers/WorkflowEmpathyController.cs
backend/Flowamaz.Tests.Unit/Workflow/Empathy/WorkflowEmpathyServiceTests.cs
web/src/components/editor/EmpathyPanel.vue
web/src/views/workflow/WorkflowEditorView.vue (empathy toggle added)
web/src/help/articles/workflow-empathy.md
```
