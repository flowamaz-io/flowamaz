---
prompt-id: 02-07-process-intelligence
phase: 02
sequence: 7
roles: [Executor, Verifier]
type: feature
depends-on: [02-06-frontend-workflow-views]
estimated-complexity: Medium
---

# Process Intelligence — Predictive Analytics + Workflow Weather

## Context
Engine running, frontend complete. Now build Process Intelligence — the predictive
analytics layer that watches run patterns and surfaces insights before problems occur.
Also build the Workflow Weather executive dashboard. See FUNCTIONAL.md §8.1 (Workflow Twin)
and §11.2 (Process Intelligence) and §11.6 (Workflow Weather).

## Objective
Hourly batch analytics job predicting SLA breaches, detecting bottlenecks, calculating
per-workflow metrics, and powering the Workflow Weather executive view.

## Scope

### What to Build

**Analytics entities (add to existing migration or new migration):**

`WorkflowMetric`:
- Id (Guid), WorkspaceId (FK), WorkflowDefinitionId (FK)
- PeriodHour (DateTime — truncated to hour)
- RunsTotal (int), RunsCompleted (int), RunsFailed (int), RunsCancelled (int)
- AvgDurationMs (long), P95DurationMs (long), P99DurationMs (long)
- SlaBreachCount (int), SlaThresholdMs (long?)
- BottleneckNodeId (string? — node with highest avg duration)
- BottleneckAvgMs (long?)
- CreatedAt

`WorkflowInsight`:
- Id (Guid), WorkspaceId (FK), WorkflowDefinitionId (FK), InstanceId (Guid?)
- InsightType (enum: SlaRisk/Bottleneck/AnomalyDetected/CompletionForecast/PatternChange)
- Severity (enum: Info/Warning/Critical)
- Message (string — human readable, actionable)
- Data (jsonb — supporting data for the insight)
- IsAcknowledged (bool), AcknowledgedBy (Guid?), AcknowledgedAt (DateTime?)
- ExpiresAt (DateTime?), CreatedAt
- Index: (WorkspaceId + IsAcknowledged + CreatedAt)

**Process Intelligence Job (Quartz.NET, hourly):**

`ProcessIntelligenceJob`:

For each active workspace, for each workflow with runs in last 24h:

1. Compute metrics for the last hour:
   - Aggregate from WorkflowInstance table
   - Calculate p95/p99 from duration distribution
   - Find bottleneck node (highest avg duration from WorkflowNodeState)
   - Upsert WorkflowMetric row

2. Generate insights using Claude Haiku (F5 — Process Intelligence function):
   System prompt: "You are a workflow analytics assistant. Given workflow metrics,
   identify actionable insights. Return JSON only: [{type, severity, message, data}]"
   Input: last 7 days of WorkflowMetric rows for this workflow (structured JSON, small)
   Output: list of insights to create
   Cost: ~$0.0001 per workflow per hour. Batch: one call per workflow, not per instance.
   Cache: semantic cache keyed on metric hash — same metrics = cached insight.

3. SLA breach prediction (deterministic — no AI):
   If avg_duration_ms > sla_threshold_ms * 0.8 → create SlaRisk insight
   "Your {workflow_name} is averaging {X}h — 80% of your {Y}h SLA. At current rate,
   SLA will breach within {Z} runs."

4. Upsert insights (replace existing unacknowledged insights of same type for same workflow)

**Workflow Weather API:**

`GET /api/v1/workspaces/{workspaceId}/weather` [Operator]:
Returns health status per workflow:
```json
{
  "generated_at": "...",
  "workflows": [
    {
      "workflow_id": "...", "workflow_name": "...",
      "status": "green|yellow|orange|red",
      "active_instances": 3,
      "failed_last_hour": 0,
      "sla_compliance_pct": 98,
      "pending_insights": 1,
      "latest_insight": "..."
    }
  ]
}
```
Status logic:
- green: no failures, SLA compliance > 95%, no Critical insights
- yellow: SLA compliance 80-95% OR Warning insights
- orange: SLA compliance 60-80% OR 1+ failures in last hour
- red: SLA compliance < 60% OR Critical insights OR 3+ failures in last hour

`GET /api/v1/workspaces/{workspaceId}/insights` [Operator]:
Paginated list of WorkflowInsights, filterable by severity/acknowledged/workflow.

`POST /api/v1/workspaces/{workspaceId}/insights/{id}/acknowledge` [Operator]:
Marks insight as acknowledged.

**Frontend — Workflow Weather widget (add to dashboard):**

`FmWorkflowWeather.vue` — shown on the main dashboard.
Grid of workflow cards, each showing:
- Workflow name
- Coloured status dot (green/yellow/orange/red)
- Active instances count
- Latest insight message (if any)
- Click → WorkflowDetailView

Replace the placeholder metric cards from Phase 1 dashboard with real data:
- Total workflows (from workflow list API)
- Active instances (from instance list filtered by Running/Waiting)
- Runs this month (from metrics)
- Team members (already wired from Phase 1)

**Unit tests:**
- ProcessIntelligenceJob: SLA risk calculation (deterministic, no AI)
- WorkflowWeather status calculation: all 4 status conditions
- Insight upsert: existing unacknowledged insight replaced (not duplicated)

## Technical Requirements
- [ ] Process Intelligence job uses F5 model via IModelResolutionService — not hardcoded
- [ ] AI call tokens metered via IAiTokenMeteringService
- [ ] Semantic cache: same metric hash → cached insight response (no duplicate AI calls)
- [ ] SLA breach detection is deterministic (no AI) — runs every time regardless
- [ ] Quartz job: hourly, with 5-minute jitter to avoid thundering herd
- [ ] Workflow Weather: status calculated server-side (not in frontend)
- [ ] Dashboard metric cards: all 4 wired to real API data

## Acceptance Criteria
- [ ] ProcessIntelligenceJob runs on schedule (log confirms)
- [ ] SLA risk insight created when avg_duration > sla_threshold * 0.8
- [ ] Workflow Weather: red status when 3+ failures in last hour
- [ ] Dashboard: "Active instances" card shows real count from API
- [ ] FmWorkflowWeather renders all workflow cards with correct colour status

## Output Expected
```
backend/Flowamaz.Core/Entities/Analytics/ (WorkflowMetric, WorkflowInsight)
backend/Flowamaz.Core/Enums/ (InsightType, InsightSeverity)
backend/Flowamaz.Application/Analytics/ProcessIntelligenceJob.cs
backend/Flowamaz.Api/Controllers/WorkflowWeatherController.cs
backend/Flowamaz.Api/Controllers/WorkflowInsightsController.cs
backend/Flowamaz.Tests.Unit/Analytics/ProcessIntelligenceJobTests.cs
backend/Flowamaz.Tests.Unit/Analytics/WorkflowWeatherTests.cs
web/src/components/dashboard/FmWorkflowWeather.vue
web/src/views/dashboard/DashboardView.vue (updated with real data)
```
