---
prompt-id: 03-06-workflow-editor-view
phase: 03
sequence: 6
roles: [Executor, Verifier, UI, UX]
type: feature
depends-on: [03-05-copilot-sop-creation]
estimated-complexity: High
---

# Workflow Editor View — Canvas + YAML Editor + Co-pilot Panel + Help

## Context
All creation methods and the canvas component exist. Now build the complete
workflow editor view that combines the SFG canvas, CodeMirror YAML editor,
Co-pilot command panel, and node help system.

## Objective
The WorkflowEditorView that is the primary workflow authoring experience.
Split pane: canvas left, YAML editor right. Co-pilot panel slides up from bottom.
Real-time validation. Canvas node help. Workflow health score live update.

## Scope

### What to Build

**WorkflowEditorView (/workflows/:id/edit):**

Layout: full-screen editor, no sidebar (sidebar collapses automatically on edit open).

Split pane (resizable, default 60% canvas / 40% YAML):
- Left: SfgCanvas.vue
- Right: YAML editor (CodeMirror 6 with custom FlowAmaz language support)
- Drag divider to resize
- Toggle: hide YAML panel (canvas full-width) / hide canvas (YAML full-width)

**CodeMirror 6 YAML Editor (FmYamlEditor.vue):**

Extensions to configure:
- @codemirror/lang-yaml (base YAML syntax)
- Custom FlowAmaz linting extension (calls /validate API, shows squiggles)
- Custom hover extension (field name hover → tooltip with description from yaml-spec.md)
- Custom completion extension (Ctrl+Space → autocomplete node IDs, variable names)
- Custom gutter (line numbers + validation severity icons)
- Theme: match Tailwind dark/light mode

Validation integration:
- On change (debounced 800ms): POST /validate → apply diagnostics to editor
- Error squiggles: red (Layer 1-5 errors), yellow (Layer 6 warnings)
- Hover squiggle → tooltip with error code + message + "Learn more" link

Field help tooltips:
- Hover `type:` → tooltip: "Node type. Valid values: action, ai, human-gate, router, end, annotation"
- Hover `retry:` → tooltip: "Retry policy. Applies to action and ai nodes." + link to YAML spec
- All top-level fields and node config fields documented via hover

Snippet library (Ctrl+Space or typing a prefix):
- `action:` → expands full action node template
- `ai:` → expands AI node template
- `gate:` → expands human-gate template
- `router:` → expands router with 2 outputs
- `foreach:` → forEach block
- `trycatch:` → try-catch block
- Triggered by typing the node type prefix

**Canvas ↔ YAML sync (two-directional, already in useYamlCanvasSync):**
In editor view: cursor position in YAML → highlight corresponding node on canvas.
Click node on canvas → CodeMirror cursor jumps to that node's YAML block.

**Co-pilot Panel (slides up from bottom):**

Trigger: Cmd+K / Ctrl+K keyboard shortcut OR "✦ Co-pilot" button in toolbar.
Panel slides up 40% screen height with animation.

Input: "What would you like to do?" text field.
Autocomplete suggestions shown as user types (recent commands from localStorage).

On submit:
1. POST /copilot with command + current yamlContent
2. Show streaming response (tokens appear as they arrive)
3. If pattern match: instant result, show "Applied pattern: {pattern_name}"
4. If AI: show streaming output, then apply patch to YAML
5. Success: show diff preview ("What changed") before applying
6. Apply button → YAML + canvas update
7. Undo: Ctrl+Z reverts Co-pilot change (goes into undo history)

Co-pilot history: last 10 commands shown in panel. Click to re-apply.

**Canvas Node Help (right-click → "Help with this node type"):**

When right-clicking any node → context menu includes "Help with this node type".
Opens HelpPanel (Shift+?) to the article for that specific node type:
- Trigger node → help article: "node-types/trigger-nodes"
- Action node → "node-types/action-nodes"
- AI node → "node-types/ai-nodes"
- Human Gate → "node-types/human-gate-nodes"
- Router → "node-types/router-nodes"

Add these 6 new help articles to web/src/help/articles/node-types/.
Article content: what the node does, config fields explained, YAML example, common mistakes.

Add to articleMap.ts:
```ts
'/workflows/:id/edit': 'getting-started/using-the-canvas',
'/workflows/:id/edit#node:trigger': 'node-types/trigger-nodes',
// etc.
```

**Toolbar additions:**
- Health score badge (live, from GET /workflows/:id which includes health_score)
- Validate button (manual trigger of /validate)
- "✦ Co-pilot" button (opens Co-pilot panel)
- Save button shows "Saved" / "Unsaved changes" / "Saving..." state

**Keyboard shortcuts summary (shown in Help panel):**
Ctrl+S: save, Ctrl+Z: undo, Ctrl+Y: redo, Ctrl+K: Co-pilot,
Shift+?: help, Delete: delete selected, Ctrl+A: select all

## Technical Requirements
- [ ] Split pane: resizable, remembers proportion in localStorage
- [ ] CodeMirror: real-time validation squiggles (debounced 800ms)
- [ ] CodeMirror: field hover tooltips for all YAML spec fields
- [ ] CodeMirror: Ctrl+Space → snippet picker
- [ ] Co-pilot: Ctrl+K shortcut, streaming response
- [ ] Co-pilot: diff preview before applying patch
- [ ] Co-pilot: Ctrl+Z reverts Co-pilot change
- [ ] Canvas node help: right-click → help panel opens to correct article
- [ ] 6 node-type help articles written (complete, not placeholder)
- [ ] Health score badge: real-time from API
- [ ] Zero style blocks

## Acceptance Criteria
- [ ] Editor loads: canvas left, YAML right, both showing same workflow
- [ ] Edit YAML → canvas updates (debounced)
- [ ] Click node on canvas → CodeMirror cursor moves to node block
- [ ] Ctrl+K → Co-pilot panel opens, accepts command, shows result
- [ ] Validation error → red squiggle in CodeMirror, correct error message
- [ ] Right-click trigger node → Help → opens trigger-nodes article
- [ ] Health score badge shows current score

## Output Expected
```
web/src/views/workflow/WorkflowEditorView.vue
web/src/components/editor/FmYamlEditor.vue
web/src/components/editor/CopilotPanel.vue
web/src/composables/useWorkflowEditor.ts
web/src/help/articles/node-types/trigger-nodes.md (+ 5 others)
web/src/help/articleMap.ts (updated)
web/src/tests/FmYamlEditor.test.ts
web/src/tests/CopilotPanel.test.ts
```
