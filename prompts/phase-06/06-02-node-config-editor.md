---
prompt-id: 06-02-node-config-editor
phase: 06
sequence: 2
roles: [Executor, Verifier, UX, UI]
type: feature
depends-on: [06-01-webhook-triggers]
estimated-complexity: High
---

# Node Config Editor — Full Connector Configuration Per Node

## Context
The canvas context menu has "Edit", "Configure action", "Configure gate" etc as stubs
since Phase 3. Node config is stored in YAML as `config: {}` empty objects.
This prompt builds the full node configuration editor — a slide-in panel from the right
that lets users configure each node's connector, inputs, outputs, and settings.

## Objective
Complete node configuration UI for all node types. Config is stored in the workflow YAML
node.config field. Save updates the YAML and triggers canvas re-render.

## Scope

### What to Build

**NodeConfigPanel.vue — slide-in panel from the right:**

Opens when user clicks Edit on a node or double-clicks a node.
Width: 480px, full height, slides in with 250ms ease transition.
Has its own save button — does not auto-save (user must confirm).

**Panel structure:**
```
┌────────────────────────────────┐
│ [node type icon] Node Label  × │  ← header with close button
│─────────────────────────────── │
│ Basic                          │  ← tab 1
│ Inputs                         │  ← tab 2
│ Outputs                        │  ← tab 3
│ Advanced                       │  ← tab 4
│────────────────────────────────│
│ [tab content]                  │
│                                │
│────────────────────────────────│
│ [Cancel]          [Save node]  │  ← footer
└────────────────────────────────┘
```

**Per node type — Basic tab:**

`trigger` node:
- Trigger type selector: Manual | Webhook | Schedule | Event
- If Webhook: shows the webhook URL (readonly, links to webhook settings)
- If Schedule: cron expression input with human-readable preview
  ("0 9 * * 1-5" → "Every weekday at 9:00 AM")
- If Manual: no extra config

`action` node:
- Connector selector dropdown (lists installed connectors from workspace)
- Action selector (based on selected connector's manifest)
- Config fields (dynamic — rendered from connector manifest's input schema)
- Example: HTTP/REST connector shows: Method, URL, Headers, Body

`human-gate` node:
- Assignee type: Role | Specific user | Variable
- If Role: role selector (Admin/Designer/Operator/Viewer)
- If Specific user: user search (workspace members)
- If Variable: variable name input (e.g. `${approver_email}`)
- Options: list of decision options (approve/reject/revert) — add/remove/rename
- Notification channels: email toggle, Slack toggle, Teams toggle, Portal (always on)
- Timeout: duration input (hours/days selector + number) + on_timeout action

`router` node:
- Condition builder for each output branch
- Each branch: label + condition expression
- Expression input with variable autocomplete from workflow variables
- [+ Add branch] button
- Default branch toggle (no condition — catch-all)

`ai` node:
- Provider selector (Anthropic/Google/BYOM)
- Model selector (based on provider)
- System prompt textarea
- User prompt template (supports ${variable} interpolation)
- Max tokens slider
- Output variable name (where to store AI response)

`end` node:
- Outcome label (success/failure/custom)
- Outcome description

**Inputs tab (all nodes except trigger/end):**
- Variable mapping: map workflow variables to node input fields
- Each row: [node input field] ← [workflow variable or literal value]
- Add/remove mappings

**Outputs tab (action/ai nodes):**
- Define what this node outputs
- Each row: [output key] → [workflow variable to store in]
- Example: HTTP action → response.body → ${invoice_data}

**Advanced tab:**
- Retry policy: count + delay + backoff (exponential/linear)
- Error handling: continue/stop/goto-node
- Timeout (if not set in Basic)
- Tags (for filtering in instances view)

**YAML update on save:**
When user clicks "Save node":
- Merge the config into the current YAML node entry
- Preserve all other node fields (id, type, label, edges)
- Trigger canvas re-render
- Mark editor as dirty (unsaved to backend)

**Variable autocomplete:**
In any text input that supports ${} interpolation:
- Type `${` → dropdown appears with all workflow variables
- Filter as user types
- Select inserts `${variableName}`
- Shows variable type next to each suggestion

**Unit tests:**
- NodeConfigPanel: trigger type change updates YAML config
- NodeConfigPanel: human-gate assignee type change renders correct fields
- NodeConfigPanel: save merges config into YAML without losing other fields
- RouterConditionBuilder: add branch adds correct edge condition
- VariableAutocomplete: shows variables matching filter

## Technical Requirements
- [ ] Zero style blocks — all Tailwind
- [ ] Panel slide-in: translate-x-full → translate-x-0 with transition
- [ ] Dynamic form fields rendered from connector manifest input schema
- [ ] Variable autocomplete in all expression/template fields
- [ ] Config saved to YAML, not a separate API call
- [ ] Panel accessible: keyboard navigable, focus trap while open

## Acceptance Criteria
- [ ] Double-click any node → config panel opens
- [ ] Configure human-gate assignee → saved in YAML config
- [ ] Router: add condition branch → new conditional edge in YAML
- [ ] Variable autocomplete: type `${` → dropdown shows workflow variables
- [ ] Save → YAML updated, canvas re-renders

## Output Expected
```
web/src/components/canvas/NodeConfigPanel.vue
web/src/components/canvas/config/TriggerConfig.vue
web/src/components/canvas/config/ActionConfig.vue
web/src/components/canvas/config/HumanGateConfig.vue
web/src/components/canvas/config/RouterConditionBuilder.vue
web/src/components/canvas/config/AiNodeConfig.vue
web/src/components/canvas/config/EndConfig.vue
web/src/components/canvas/shared/VariableAutocomplete.vue
web/src/components/canvas/shared/InputMappingEditor.vue
web/src/tests/NodeConfigPanel.test.ts
```
