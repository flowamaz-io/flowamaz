---
prompt-id: 07-04-advanced-monitoring
phase: 07
sequence: 4
roles: [Executor, Verifier, UX]
type: feature
depends-on: [07-03-workflow-templates]
estimated-complexity: High
---

# Advanced Instance Monitoring — Step Debugger, Replay, Breakpoints

## Context
Instance detail view shows a timeline (Phase 2) but lacks debugging capability.
Developers need to see exactly what happened at each step — inputs, outputs, errors —
and replay failed instances with modified payloads.

## Objective
Step debugger showing per-node I/O, instance replay with payload override,
and breakpoints for development environments.

## Scope

### What to Build

**Per-node execution data:**

WorkflowEvent entity extension (already exists):
Add to existing WorkflowEvent:
- InputSnapshot (jsonb) — inputs to this node at execution time
- OutputSnapshot (jsonb) — outputs produced by this node
- ErrorSnapshot (jsonb) — error details if node failed
- DurationMs (int) — node execution duration

Update WorkflowOrchestrator to capture snapshots:
Before executing each node: snapshot inputs from variable context
After executing each node: snapshot outputs, duration
On error: snapshot error (message + type, NOT stack trace to client)

**Step Debugger UI:**

In InstanceDetailView.vue — Timeline tab:

Current: shows nodes as a flat list with status badges.

Improved: interactive timeline with expandable nodes:

```
▶ Fund Status Update Form Submitted (trigger)     0ms
▶ Notify Approver (action)                        142ms
  ▼ INPUTS                                        
    { "recipient": "approver@company.com" }
  ▼ OUTPUTS                                       
    { "email_id": "msg_abc123", "delivered": true }
⏸ Approver Review (human-gate)                   2d 4h [PENDING]
  Waiting for: jagan@tootker.com
  Options: approve | reject | revert
  Timeout: 2026-06-01 09:00 UTC
```

Node row click → expand/collapse I/O snapshots
Colour coding: green (complete), amber (pending/waiting), red (failed), gray (skipped)

**Instance Replay:**

`ReplayInstanceAsync(instanceId, payloadOverride, workspaceId, ct)`:
- Only available on terminal instances (Completed/Failed/Cancelled)
- Creates a new WorkflowInstance with:
  - Same workflow version as original
  - Original payload merged with payloadOverride
  - IsTest = true (replay is always a test run)
  - ParentInstanceId = original instanceId (lineage tracking)
- Returns new instanceId

`POST /api/v1/workspaces/{id}/instances/{instanceId}/replay`
[RequireWorkspaceRole(Designer)]
Body: { payloadOverride: {} }

`ReplayButton.vue` in InstanceDetailView:
- Only shown on terminal instances
- Click → modal with JSON editor pre-filled with original payload
- User can edit payload → [Replay] → creates test instance → navigates to new instance

**Breakpoints (Development environment only):**

In ASPNETCORE_ENVIRONMENT=Development:
WorkflowBreakpoint entity (in-memory only, not persisted):
- WorkflowId + NodeId → pause before executing this node

`POST /api/v1/dev/breakpoints` — set breakpoint
`DELETE /api/v1/dev/breakpoints/{id}` — remove breakpoint
`GET /api/v1/dev/breakpoints` — list active breakpoints

On hitting a breakpoint:
- Instance pauses (status: BreakpointHit)
- Emits WebSocket event: { type: "breakpoint_hit", nodeId, context }
- Developer can inspect variable context, then:
  `POST /api/v1/dev/instances/{id}/resume` — continue execution
  `POST /api/v1/dev/instances/{id}/step` — execute one node then pause again

Canvas integration: nodes with active breakpoints shown with a red dot on the canvas.

**Variable Inspector:**

New tab in InstanceDetailView: Variables
Shows the workflow variable context at the END of execution (or current state if running):
```
requester_email    "jagan@tootker.com"
fund_name          "Tech Growth Fund"
approval_decision  "approved"
```

On running instances: polls every 5 seconds.
On completed instances: static snapshot from last WorkflowEvent.

**Unit tests:**
- WorkflowOrchestrator: input/output snapshots captured on each node
- ReplayService: creates new test instance with merged payload
- ReplayService: replay of running instance → 409 Conflict
- BreakpointService: breakpoint hit pauses instance
- BreakpointService: only available in Development environment

## Technical Requirements
- [ ] Snapshots stored in WorkflowEvent (jsonb columns)
- [ ] Sensitive variables stripped from snapshots (marked sensitive: true in YAML)
- [ ] Replay: always creates test instance (never production)
- [ ] Breakpoints: Development only, in-memory, not persisted
- [ ] Variable inspector: polls on running, static on completed

## Acceptance Criteria
- [ ] Click node in timeline → I/O snapshot expands
- [ ] Replay failed instance → new test instance created with same payload
- [ ] Replay with override → modified payload used
- [ ] Sensitive variables: not visible in snapshots
- [ ] Variable inspector shows current workflow context

## Output Expected
```
backend/Flowamaz.Application/Workflow/Services/ReplayService.cs
backend/Flowamaz.Api/Controllers/InstanceDebugController.cs
backend/Flowamaz.Tests.Unit/Workflow/ReplayServiceTests.cs
web/src/views/instance/InstanceDetailView.vue (timeline enhanced)
web/src/components/instance/StepDebugger.vue
web/src/components/instance/ReplayModal.vue
web/src/components/instance/VariableInspector.vue
```
