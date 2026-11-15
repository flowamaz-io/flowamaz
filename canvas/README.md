# @flowamaz/canvas

The Flowamaz workflow canvas for Vue 3 (MIT). It renders workflow YAML (`apiVersion: flowamaz/v1`, see `docs/yaml-spec.md`) as a graph with [Cytoscape.js](https://js.cytoscape.org), and writes canvas edits back to the YAML without disturbing comments or formatting.

What's inside:
- **Model:** `createCanvasModel()` holds nodes, edges, groups, selection, the YAML text and undo history. In an app with Pinia, use `defineStore('canvas', () => createCanvasState())`.
- **YAML ↔ graph:** `parseWorkflowGraph`, `workflowSection`, `canvasNodeType`.
- **Edits:** `patchWorkflowYaml` (changes only the lines an edit touches), `mergeText` (three-way merge), `textChanges`.
- **Canvas:** `useCytoscapeCanvas(model, containerRef, options)` handles layout, selection, the Connect gesture, grouping, snapping and the viewport. `useYamlCanvasSync(model, getCy)` keeps YAML and graph in step. `useCanvasHistory(model)` gives undo and redo.
- **Components:** `NodePalette`, `CanvasToolbar`, `NodeContextMenu`, `CanvasMinimap`.
- **Geometry:** grid snapping and minimap maths (`snapPosition`, `computeMinimapTransform`, …).

```ts
import { ref } from 'vue';
import { createCanvasModel, useCytoscapeCanvas, useYamlCanvasSync } from '@flowamaz/canvas';

const model = createCanvasModel();
const container = ref<HTMLElement | null>(null);
const { cy } = useCytoscapeCanvas(model, container);
const sync = useYamlCanvasSync(model, () => cy.value);
```

Notes:
- The package ships TypeScript and Vue single-file components as source, so use it with a bundler that compiles them (Vite with `@vitejs/plugin-vue`).
- The components use Tailwind CSS classes. Add the package to your Tailwind sources, for example `@source "../node_modules/@flowamaz/canvas/src";`.

The full editor in the Flowamaz app (configuration panels, live collaboration, server saves) is built on this package and isn't part of it.

```bash
npm install && npm test && npm run typecheck
```
