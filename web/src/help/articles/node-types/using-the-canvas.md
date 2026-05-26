# Using the Workflow Canvas

The canvas is the visual editor for your workflow. It shows nodes and edges, and stays in sync with the YAML editor.

## Navigation
- **Pan** — click and drag on the background
- **Zoom** — scroll wheel or pinch gesture; Ctrl+= / Ctrl+-
- **Fit to view** — press F or click the fit button in the toolbar
- **Minimap** — toggle in the toolbar; click the minimap to jump to any area

## Editing
- **Add node** — drag from the node palette on the left onto the canvas
- **Connect nodes** — hover a node's edge handle (blue dot), then drag to another node
- **Delete** — select nodes/edges and press Delete or Backspace
- **Multi-select** — hold Shift and click, or drag a selection rectangle
- **Undo / Redo** — Ctrl+Z / Ctrl+Y (full undo history including canvas moves)

## YAML sync
Every canvas change updates the YAML editor instantly. Every YAML edit updates the canvas (debounced 300ms). If YAML has a parse error, the canvas shows the last valid state.

## Right-click menu
Right-click any node to see:
- **Edit properties** — opens the node inspector
- **Help with this node type** — opens the help panel for this node type
- **Delete node** — removes the node and all connected edges

## Keyboard shortcuts
| Shortcut | Action |
|----------|--------|
| Ctrl+S | Save workflow |
| Ctrl+Z | Undo |
| Ctrl+Y | Redo |
| Ctrl+K | Open Co-pilot |
| Ctrl+A | Select all |
| Delete | Delete selected |
| Shift+? | Open help panel |
| F | Fit canvas to view |
