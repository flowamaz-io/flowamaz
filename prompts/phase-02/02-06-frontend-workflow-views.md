---
prompt-id: 02-06-frontend-workflow-views
phase: 02
sequence: 6
roles: [Executor, Verifier, UI, UX]
type: feature
depends-on: [02-05-workflow-interpreter]
estimated-complexity: High
---

# Frontend — Workflow List, Instance List, Run Timeline, Interpreter UI

## Context
Backend engine and all APIs complete. Now build the Vue 3 frontend views that expose
the workflow engine to users. The canvas (Phase 3) is not in scope here — this prompt
covers list views, instance monitoring, run timeline, and the Workflow Interpreter UI.

## Objective
Workflow definition list, instance list with live status, run timeline waterfall view,
Workflow Interpreter narrative panel, and instance detail view with node states.

## Scope

### What to Build

**WorkflowListView (/workflows):**
- Table: name, slug, status badge (Draft/Published/Archived), health score (coloured 0-100),
  last updated, trigger count (month), actions (view, edit, trigger, archive)
- Health score display: 80-100 = green, 60-79 = amber, 0-59 = red
- Filter: by status, search by name
- Empty state: FmEmptyState with "Create your first workflow" CTA
  — Shows all 6 creation method icons (grayed, Phase 3 builds them — coming soon)
- Trigger button: opens TriggerModal (payload JSON editor + idempotency key field)

**WorkflowDetailView (/workflows/:id):**
- Header: name, status, health score, version info, Publish button
- Tabs: Overview | Instances | Versions | YAML (read-only CodeMirror for Phase 2)
- Overview tab: nl_description, trigger type, node count, created by method badge
- Instances tab: recent instances with status and link to instance detail
- Versions tab: list of published versions with commit SHA and created date

**InstanceListView (/instances):**
- Table: instance ID (short), workflow name, status badge, started at, duration, trigger type
- Status badges with colours:
  Pending=gray, Running=blue (animated pulse), Waiting=amber, 
  Completed=green, Failed=red, Cancelled=gray, Compensating=orange
- Filter: by status, by workflow, date range
- Live updates: WebSocket connection updates status badges in real time (no page refresh)
- Row click → InstanceDetailView

**InstanceDetailView (/instances/:id):**
- Header: instance ID, status badge, duration, workflow name + version
- Tab 1 — Timeline: waterfall view (see below)
- Tab 2 — Node States: table of all nodes with status, duration, retry count
- Tab 3 — Variables: key/value table (sensitive values shown as ***)
- Tab 4 — Events: paginated event log (event_type, node_id, occurred_at, payload preview)
- Tab 5 — Narrative: Workflow Interpreter panel (see below)
- Action buttons: Cancel (if Running), Retry (if Failed)

**Run Timeline component (FmRunTimeline.vue):**
Based on GET /instances/{id}/timeline response.
Gantt-style waterfall (like Chrome DevTools network panel):
- Left column: node label + type icon + status colour
- Right: horizontal bar showing duration relative to total
- Bar position: offset_ms from instance start (left edge)
- Bar width: duration_ms as % of total_duration_ms
- Hover: tooltip showing started_at, duration_ms, retry_count
- Completed=teal, Failed=red, Running=blue animated, Waiting=amber

**Workflow Interpreter panel (FmInterpreterPanel.vue):**
- Audience selector: [CEO] [Auditor] [Developer] — tab group
- On tab click: GET /instances/{id}/narrative?audience=ceo|auditor|developer
- Loading state: spinner with "Generating narrative..."
- Content: rendered markdown (CEO/Auditor) or structured JSON tree (Developer)
- Download button: POST /narrative/download → triggers file download
- CEO tab: only available if instance is Completed (not in-progress)
- Auditor tab: available for all terminal states

**TriggerModal component:**
- Workflow selector (pre-filled if triggered from workflow detail)
- Payload: CodeMirror JSON editor (validates JSON before submit)
- Idempotency key: optional text field with "Generate" button (generates UUID)
- Submit: POST /instances → shows new instance ID + link to detail

**WebSocket integration (useInstanceWebSocket composable):**
- Connects to /ws/v1/workspaces/{id}/instances/{instanceId}?token={accessToken}
- Updates instance status and node states reactively in Pinia store
- Reconnects on disconnect (exponential backoff, max 30s)
- Shows "Live" indicator when connected, "Reconnecting..." when not

**Pinia stores (extend workspace.store or create workflow.store):**
- workflows: list, currentWorkflow, loading, error
- instances: list, currentInstance, nodeStates, variables, events, timeline
- Actions: load, trigger, cancel, retry, pollTimeline, connectWebSocket

## Technical Requirements
- [ ] Zero style blocks in any .vue file
- [ ] WebSocket reconnect: exponential backoff, max 5 retries then "Connection lost" state
- [ ] Run Timeline: correct pixel positioning (offset_ms / total_duration_ms * 100%)
- [ ] Sensitive variables: always display *** (never request unmasked)
- [ ] All list views: loading state, empty state (FmEmptyState), error state (FmErrorState)
- [ ] Health score colour: computed dynamically, not hardcoded classes
- [ ] TypeScript strict — no any types

## Acceptance Criteria
- [ ] WorkflowListView: loads workflows, trigger modal opens and submits
- [ ] InstanceListView: WebSocket updates status badge without page refresh
- [ ] Run Timeline: bars positioned correctly relative to total duration
- [ ] Interpreter: CEO narrative renders markdown, Download triggers file save
- [ ] All 3 narrative audiences load correctly from API
- [ ] npm run build — 0 TypeScript errors

## Output Expected
```
web/src/views/workflow/WorkflowListView.vue
web/src/views/workflow/WorkflowDetailView.vue
web/src/views/instance/InstanceListView.vue
web/src/views/instance/InstanceDetailView.vue
web/src/components/workflow/FmRunTimeline.vue
web/src/components/workflow/FmInterpreterPanel.vue
web/src/components/workflow/TriggerModal.vue
web/src/composables/useInstanceWebSocket.ts
web/src/stores/workflow.store.ts
web/src/services/workflow.service.ts
web/src/services/instance.service.ts
web/src/tests/FmRunTimeline.test.ts
web/src/tests/useInstanceWebSocket.test.ts
```
