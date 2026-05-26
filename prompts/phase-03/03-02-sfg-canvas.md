---
prompt-id: 03-02-sfg-canvas
phase: 03
sequence: 2
roles: [Executor, Verifier, UI, UX]
type: feature
depends-on: [03-01-yaml-spec-validator]
estimated-complexity: High
---

# SFG Canvas — Full Interactive Cytoscape.js Canvas (All 11 Interactions)

## Context
YAML spec and validator exist. Now build the full interactive SFG canvas.
This is the most complex frontend component in the entire project.
See FUNCTIONAL.md §6.6 for all 11 required interactions.

## Objective
Build the complete SFG canvas with all 11 interactions, YAML↔canvas bidirectional sync,
node inspector panel, right-click context menu, and keyboard shortcuts.
Quality bar: matches or exceeds n8n's canvas interaction quality.

## Scope

### What to Build

**SfgCanvas.vue (web/src/components/canvas/):**

Built on Cytoscape.js + cytoscape-edgehandles + cytoscape-dagre.
The canvas is the authoritative view of the workflow. YAML is derived from it.

**11 Required Interactions:**

1. **Drag nodes from palette to canvas:**
   Left sidebar palette with all node types (Trigger, Action, AI, Human Gate, Router, End,
   Annotation). Drag from palette → drop on canvas → node created at drop position.
   Use HTML5 drag API + Cytoscape.js add(). Node gets a generated ID (type-N).

2. **Drag from output handle to create edges:**
   Cytoscape-edgehandles extension. Hover node → output handle appears (small circle).
   Drag from handle → rubber-band line follows cursor → drop on target node → edge created.
   For Router nodes: prompt for output ID (inline mini-form on drop).

3. **Node move by drag:**
   Standard Cytoscape.js. Position stored as node data {x, y}.
   Canvas layout positions saved to YAML metadata.position block.

4. **Undo/redo (Ctrl+Z / Ctrl+Y):**
   History stack of YAML snapshots. Every canvas action pushes a snapshot.
   Max 50 history entries. Status bar shows "Undo (N actions available)".

5. **Auto-layout (dagre algorithm):**
   "Tidy Layout" button in toolbar. Runs cytoscape-dagre layout.
   rankDir: 'TB' (top-bottom), ranksep: 80, nodesep: 60.
   After layout: saves new positions to YAML.

6. **Edge condition labels on router outputs:**
   Router node edges display condition text as a small pill on the edge midpoint.
   Truncated to 30 chars with tooltip for full text.
   Read from edge.via → router.config.outputs[via].condition.

7. **Multi-select (rubber-band drag) + group move:**
   Click-drag on empty canvas area → rubber-band selection box.
   Shift+click → add to selection.
   Move selected nodes together.
   Delete key → remove all selected nodes + their edges (with confirm if > 1).

8. **Group containers (swimlanes):**
   "Add group" button → creates a WorkflowGroup (rounded rect, labelled, coloured).
   Drag nodes into/out of groups.
   Group stored in YAML `groups:` block.
   Groups rendered as Cytoscape compound nodes.

9. **Sticky notes (annotations):**
   Special node type `annotation`. Yellow background, italic text, no execution.
   Ignored by engine. Stored in YAML as `type: annotation`.
   Double-click annotation → inline text editor.

10. **Minimap (functional):**
    Cytoscape.js navigator extension (cytoscape-navigator).
    Bottom-right corner, 150×120px.
    Click minimap → pan canvas to that position.

11. **YAML↔canvas bidirectional sync:**
    Canvas changes → YAML updates in real time (debounced 300ms).
    YAML edits → canvas updates (parse YAML, diff nodes/edges, update canvas).
    Click node on canvas → CodeMirror jumps to that node's YAML block.
    Edit YAML → canvas updates without losing selection or scroll position.

**Node visual design:**
Each node type has distinct colour, icon (Lucide), and shape:
- Trigger: purple #534AB7, bolt icon, rounded rect
- Action: teal #1D9E75, play icon, rounded rect
- AI: blue #378ADD, brain icon, rounded rect
- Human Gate: amber #EF9F27, user-check icon, diamond
- Router: coral #E24B4A, git-branch icon, diamond
- End: gray #888780, circle-stop icon, circle
- Annotation: yellow #FBBF24, sticky-note icon, rounded rect (dashed border)

Node card shows: type icon (left), label (centre), status indicator (right — Phase 2 data).

**Node Inspector Panel (right side, slides in on node click):**

Header: node type badge, node ID, ? help icon (→ opens help panel to node article)

Tabs: Config | Retry | Timeout | Compensation

Config tab content varies by node type:
- Action: connector selector, operation selector, input mapping fields
- AI: model picker (per-function override), prompt template, output schema
- Human Gate: assignee variable, delivery channel, timeout, escalation
- Router: condition builder per output (expression input with variable picker)
- Trigger: trigger type config

All inspector fields use HelpTooltip (? icon) with field descriptions.

**Toolbar:**
Left: Save (Ctrl+S), Undo (Ctrl+Z), Redo (Ctrl+Y), Tidy Layout
Right: Zoom in/out, Fit to screen, Minimap toggle
Center: Workflow name (editable inline), Version badge

**Right-click context menu on node:**
- Edit (opens inspector)
- Duplicate node
- Add connected node → (submenu with node types)
- Help with this node type (→ help panel)
- Delete node

**Keyboard shortcuts:**
Ctrl+Z: undo, Ctrl+Y/Ctrl+Shift+Z: redo, Ctrl+S: save,
Delete/Backspace: delete selected, Escape: deselect all,
Ctrl+A: select all, Ctrl+D: duplicate selected

**Canvas state management (Pinia canvas.store):**
- nodes: CytoscapeNodeDefinition[]
- edges: CytoscapeEdgeDefinition[]
- groups: WorkflowGroup[]
- selectedNodeId: string | null
- history: string[] (YAML snapshots)
- historyIndex: number
- isDirty: boolean (unsaved changes)
- isSyncing: boolean (YAML→canvas in progress)

## Technical Requirements
- [ ] Zero style blocks — all Tailwind classes + Cytoscape.js inline styles only
- [ ] cytoscape-edgehandles for edge creation by drag
- [ ] cytoscape-dagre for auto-layout
- [ ] cytoscape-navigator for minimap
- [ ] Undo/redo: every canvas action (add, move, delete, connect) is undoable
- [ ] YAML sync: debounced 300ms, does not lose canvas scroll position
- [ ] Node inspector: opens on single click, closes on canvas click
- [ ] Multi-select: Shift+click and rubber-band both work
- [ ] Group containers: nodes can be dragged into/out of groups
- [ ] All 11 interactions work on both mouse and trackpad
- [ ] TypeScript strict — no any types
- [ ] vitest: FmRunTimeline, canvas store history (push/undo/redo)

## Acceptance Criteria
- [ ] All 11 interactions work end to end
- [ ] Undo/redo: 5 actions → undo 5 times → original state restored
- [ ] YAML↔canvas: edit node label in YAML → canvas updates label
- [ ] YAML↔canvas: move node on canvas → YAML position updates
- [ ] Router edge condition label visible on canvas
- [ ] Minimap: click updates main canvas viewport
- [ ] Group: drag node into group → node is child of group compound node
- [ ] Node inspector: ? icon → opens help panel to correct article
- [ ] npm run build — 0 TypeScript errors

## Output Expected
```
web/src/components/canvas/SfgCanvas.vue
web/src/components/canvas/NodePalette.vue
web/src/components/canvas/NodeInspector.vue
web/src/components/canvas/CanvasToolbar.vue
web/src/components/canvas/NodeContextMenu.vue
web/src/stores/canvas.store.ts
web/src/composables/useCytoscapeCanvas.ts
web/src/composables/useCanvasHistory.ts
web/src/composables/useYamlCanvasSync.ts
web/src/types/canvas.types.ts
web/src/tests/canvas.store.test.ts
web/src/tests/useCanvasHistory.test.ts
```

## Notes for Executor
- Install: npm install cytoscape cytoscape-edgehandles cytoscape-dagre cytoscape-navigator
- Cytoscape.js in Vue 3: mount in onMounted, destroy in onUnmounted.
  Store the cy instance in a ref<Core | null>.
- edgehandles: cy.edgehandles({ snap: true, snapThreshold: 15 })
- YAML sync: use a watchEffect on the canvas.store YAML, debounce with useDebounceFn from @vueuse/core
- Group compound nodes: cy.add({ group: 'nodes', data: { id: 'group-1', label: 'Stage' } })
  Child nodes: node.data('parent', 'group-1')
- Rubber-band select: cy.boxSelectionEnabled(true), cy.userPanningEnabled(true) — both can coexist
  with modifier key: hold Shift for rubber-band while normal drag pans
