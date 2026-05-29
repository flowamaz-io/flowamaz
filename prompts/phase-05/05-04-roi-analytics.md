---
prompt-id: 05-04-roi-analytics
phase: 05
sequence: 4
roles: [Executor, Verifier, UX]
type: feature
depends-on: [05-03-vscode-extension]
estimated-complexity: Medium
---

# ROI Analytics Dashboard + Process Intelligence Improvements

## Context
Process Intelligence (hourly metrics, AI insights, SLA tracking) was built in Phase 2.
Now build the ROI Analytics Dashboard that shows the business value of automation,
and extend Process Intelligence with more sophisticated anomaly detection.

## Objective
ROI Analytics Dashboard showing time saved, cost avoided, and process improvement
metrics. Extended Process Intelligence with trend analysis and anomaly detection.

## Scope

### What to Build

**ROI Analytics Entity:**

`WorkflowRoiConfig` (per workflow, workspace-scoped):
- WorkflowDefinitionId (FK), WorkspaceId (FK)
- ManualProcessTimeMinutes (int — how long this took a human before automation)
- ManualProcessCostPerRunUsd (decimal — fully-loaded cost per manual run)
- AutomationCostPerRunUsd (decimal — connector costs + AI costs + platform cost)
- MonthlyCurrency (string, default "MYR")
- CreatedAt, UpdatedAt

This lets the system calculate: time saved, cost avoided, ROI % per workflow.

**ROI Calculation Service:**

`IRoiAnalyticsService` + `RoiAnalyticsService`:

`GetWorkspaceRoiAsync(workspaceId, periodStart, periodEnd, ct)` → WorkspaceRoiSummary:

```csharp
record WorkspaceRoiSummary(
    int TotalRunsInPeriod,
    int SuccessfulRuns,
    TimeSpan TotalTimeSaved,           // ManualProcessTime * SuccessfulRuns
    decimal TotalCostAvoided,          // (ManualCost - AutomationCost) * SuccessfulRuns
    decimal RoiPercentage,             // (CostAvoided / AutomationCost) * 100
    decimal AvgCostPerRun,             // AutomationCost average
    IReadOnlyList<WorkflowRoiDetail> ByWorkflow
);
```

Calculation is deterministic — no AI, no estimation. Based on WorkflowRoiConfig
(admin-set) × actual run counts from WorkflowMetric.

**Extended Process Intelligence:**

Add to ProcessIntelligenceJob (extend, not replace):

Trend detection (deterministic):
- If failure rate increasing week-over-week: create PatternChange insight
- If avg duration increasing > 20% vs previous week: create Bottleneck insight
- If a node has failed in > 30% of runs: create specific bottleneck insight with node name

Volume anomaly (deterministic):
- If run count today is > 3 standard deviations from 30-day mean: AnomalyDetected insight
- Example: "Purchase Approval processed 847 runs today vs 23-run daily average — unusual spike"

**API endpoints:**

`GET /api/v1/workspaces/{workspaceId}/analytics/roi?from={date}&to={date}`
[RequireWorkspaceRole(Operator)]

`GET /api/v1/workspaces/{workspaceId}/workflows/{id}/roi`
[RequireWorkspaceRole(Operator)]

`PUT /api/v1/workspaces/{workspaceId}/workflows/{id}/roi-config`
[RequireWorkspaceRole(Admin)]
Body: WorkflowRoiConfig

**Frontend — ROI Analytics View (/analytics):**

New sidebar item: "Analytics" (BarChart2 icon, Lucide).

`RoiAnalyticsView.vue`:
- Date range picker (This month / Last 3 months / Last 12 months / Custom)
- Summary strip: Total time saved | Cost avoided | ROI % | Successful runs
- Per-workflow table:
  | Workflow | Runs | Time Saved | Cost Avoided | ROI % | Configure |
- [Configure] → opens ROI Config modal (set manual process time + cost)
- Chart: monthly ROI trend (recharts BarChart)
- Export CSV button

`RoiConfigModal.vue`:
- "How long did this process take manually?" — number input (minutes)
- "What was the fully-loaded cost per manual run?" — currency input
- "What currency?" — selector (MYR, USD, SGD, AUD etc)
- Save → updates WorkflowRoiConfig

**Dashboard update:**
Add ROI summary card to main dashboard (shows this month's total cost avoided).
Replace the "--" placeholder with real data from /analytics/roi.

**Help article:**
`web/src/help/articles/analytics/roi-dashboard.md`
"Understanding your ROI Dashboard" — how to configure, how numbers are calculated.

**Unit tests:**
- RoiAnalyticsService: correct time saved calculation
- RoiAnalyticsService: ROI % formula correct
- ProcessIntelligenceJob: failure rate increase → PatternChange insight created
- ProcessIntelligenceJob: volume spike → AnomalyDetected insight

## Technical Requirements
- [ ] ROI calculation is deterministic — no AI, no estimation
- [ ] RoiConfig: admin-only to configure (financial data)
- [ ] Charts: recharts (already in frontend dependencies)
- [ ] Export CSV: server-side generation, file download
- [ ] Dashboard ROI card: wired to real data

## Acceptance Criteria
- [ ] Configure ROI → view shows time saved and cost avoided with correct numbers
- [ ] ROI % formula: (CostAvoided / AutomationCost) × 100 correct in unit test
- [ ] Volume spike detection: run count > 3 std deviations → AnomalyDetected insight
- [ ] /analytics route renders chart and table

## Output Expected
```
backend/Flowamaz.Core/Entities/Analytics/WorkflowRoiConfig.cs
backend/Flowamaz.Application/Analytics/RoiAnalyticsService.cs
backend/Flowamaz.Api/Controllers/RoiAnalyticsController.cs
backend/Flowamaz.Tests.Unit/Analytics/RoiAnalyticsServiceTests.cs
web/src/views/analytics/RoiAnalyticsView.vue
web/src/components/analytics/RoiConfigModal.vue
web/src/help/articles/analytics/roi-dashboard.md
```
