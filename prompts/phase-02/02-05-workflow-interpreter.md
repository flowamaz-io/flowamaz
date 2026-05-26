---
prompt-id: 02-05-workflow-interpreter
phase: 02
sequence: 5
roles: [Executor, Verifier, UX]
type: feature
depends-on: [02-04-instance-api]
estimated-complexity: Medium
---

# Workflow Interpreter — Stakeholder Narratives + Run Timeline + Step Debugger

## Context
Instance API complete. Now build the three observability features from FUNCTIONAL.md §8.10:
Workflow Interpreter (audience-specific narratives), Run Timeline (waterfall view),
and Step Debugger (pause/inspect for dev/staging). These are Phase 2's magic features.

## Objective
Build the Workflow Interpreter service generating CEO/Auditor/Developer narratives
from execution data, the run timeline API powering the frontend waterfall view,
and the step debugger for dev/staging environments.

## Scope

### What to Build

**Workflow Interpreter (Flowamaz.Application/Workflow/Interpreter/):**

`IWorkflowInterpreterService` + `WorkflowInterpreterService`:

`GenerateNarrativeAsync(instanceId, audience, ct)` → InterpreterNarrative

Three audiences from FUNCTIONAL.md §8.10:

CEO narrative:
- Plain English status: "Your purchase request for RM47,500 is waiting for CFO approval."
- Expected completion: "Based on current queue, expected by tomorrow 3pm."
- No technical details. Uses Claude Haiku (F5 — process intelligence function).
- System prompt instructs: brief, business-focused, no jargon, use actual values from variables.

Auditor narrative:
- Formal compliance document format
- Every step with: timestamp, actor, system, decision, outcome
- Example: "On 14 March 2026 at 14:23 MYT, purchase request RM47,500 was submitted
  by Anita Kumar (HR-042). Validation against BambooHR returned active employee status.
  CFO Rajan Mehta approved at 14:31 MYT. Purchase Order FM-20026 created in SAP."
- Generated deterministically from WorkflowEvent log — no AI needed for this audience
- Format as structured markdown document

Developer narrative:
- Full technical execution log
- Per node: node_id, node_type, started_at, duration_ms, input_payload, output_payload,
  retry_count, worker_id
- Shows exactly what the engine did at each step
- Generated from WorkflowNodeState + WorkflowEvent — no AI needed

AI cost for Interpreter: only CEO narrative uses Claude Haiku (F5). Cost ~$0.0001 per call.
Auditor and Developer narratives are deterministic — zero AI cost.

**API endpoint:**
`GET /api/v1/workspaces/{workspaceId}/instances/{id}/narrative?audience=ceo|auditor|developer`
[Operator] — returns InterpreterNarrative { audience, content (markdown), generated_at }
Add `POST /narrative/download` returning the narrative as a downloadable PDF (use QuestPDF
or a simple HTML-to-PDF approach — keep it simple for Phase 2).

**Run Timeline (already partially available — enrich it here):**

`GET /api/v1/workspaces/{workspaceId}/instances/{id}/timeline`
Returns a waterfall-style response showing each node execution:
```json
{
  "instance_id": "...",
  "total_duration_ms": 4230,
  "started_at": "2026-05-25T14:23:00Z",
  "completed_at": "2026-05-25T14:23:04Z",
  "nodes": [
    {
      "node_id": "submit-request",
      "node_type": "Trigger",
      "label": "Purchase Request Submitted",
      "status": "Completed",
      "started_at": "...", "completed_at": "...",
      "duration_ms": 12,
      "offset_ms": 0,      // ms from instance start
      "retry_count": 0,
      "has_output": true
    }
  ]
}
```
Calculated from WorkflowNodeState joined with WorkflowEvent timestamps.
Frontend uses this to render the DevTools-style network waterfall.

**Step Debugger (dev/staging environments only):**

`IStepDebuggerService` + `StepDebuggerService`:

`PauseInstanceAsync(instanceId, afterNodeId, ct)` — sets a pause point
  - Stores pause point in Redis: `fmz:debug:{instanceId}:pause_after={nodeId}`
  - When OrchestratorWorker reaches this node and completes it: pauses before next node
  - Instance stays in Running status but worker holds (checks Redis before each step)

`InspectInstanceAsync(instanceId, ct)` → DebugSnapshot
  - Returns: current node states, all variables (unmasked — admin only), event log tail,
    next eligible nodes, graph position

`ForceVariableAsync(instanceId, variableName, value, ct)`
  - Overrides a variable value while instance is paused
  - Used to test "what if amount was 10000 instead of 5000"

`StepForwardAsync(instanceId, ct)` — resumes execution for exactly one more node

`ForceBranchAsync(instanceId, routerNodeId, targetBranchEdgeId, ct)`
  - Forces a router to take a specific branch regardless of condition
  - Used to test non-happy-path branches

`ResumeAsync(instanceId, ct)` — clears all pause points, resumes normal execution

**Security:** debugger endpoints require WorkspaceAdmin role AND environment must be Dev or Staging.
Production instances cannot be paused or force-varied. Returns 403 if attempted on Production.

**API endpoints:**
```
POST /api/v1/workspaces/{workspaceId}/instances/{id}/debug/pause
GET  /api/v1/workspaces/{workspaceId}/instances/{id}/debug/inspect  [Admin, Dev/Staging only]
POST /api/v1/workspaces/{workspaceId}/instances/{id}/debug/step
POST /api/v1/workspaces/{workspaceId}/instances/{id}/debug/force-branch
POST /api/v1/workspaces/{workspaceId}/instances/{id}/debug/resume
```

**Unit tests:**
- WorkflowInterpreterService: CEO narrative contains variable values from instance
- WorkflowInterpreterService: Auditor narrative contains all node timestamps
- StepDebuggerService: pause point stored in Redis, worker honours it
- StepDebuggerService: Production environment → 403 on all debugger endpoints

## Technical Requirements
- [ ] CEO narrative uses IModelResolutionService(F5) — not hardcoded claude-haiku-4-5
- [ ] CEO narrative AI call: tokens metered via IAiTokenMeteringService
- [ ] Auditor narrative: zero AI calls — deterministic from event log
- [ ] Developer narrative: zero AI calls — from node states
- [ ] Step debugger: only Dev/Staging environments — enforced at service layer not just controller
- [ ] Pause points stored in Redis with 1-hour TTL (debug sessions expire)
- [ ] ForceVariable: creates/updates WorkflowVariable — appended to event log

## Acceptance Criteria
- [ ] CEO narrative: contains instance-specific data (amounts, names from variables)
- [ ] Auditor narrative: contains all node timestamps and actor names in compliance format
- [ ] Timeline: nodes ordered by offset_ms, total_duration_ms is sum of all durations
- [ ] Debugger: pause → inspect shows current variables → step → next node executes
- [ ] Debugger: Production instance → 403 on all debug endpoints

## Output Expected
```
backend/Flowamaz.Core/Interfaces/Workflow/IWorkflowInterpreterService.cs
backend/Flowamaz.Core/Interfaces/Workflow/IStepDebuggerService.cs
backend/Flowamaz.Application/Workflow/Interpreter/WorkflowInterpreterService.cs
backend/Flowamaz.Application/Workflow/Debugger/StepDebuggerService.cs
backend/Flowamaz.Api/Controllers/WorkflowInterpreterController.cs
backend/Flowamaz.Api/Controllers/WorkflowDebuggerController.cs
backend/Flowamaz.Tests.Unit/Workflow/WorkflowInterpreterServiceTests.cs
backend/Flowamaz.Tests.Unit/Workflow/StepDebuggerServiceTests.cs
```
