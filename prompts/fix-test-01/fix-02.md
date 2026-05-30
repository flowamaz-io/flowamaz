Read CLAUDE.md and FUNCTIONAL.md.
Fix the following issues found during live testing. Fix all in one session.

--- FIX 1: Workspace selector shows empty dropdown (Image 3) ---

In web/src/components/layout/ (WorkspaceSwitcher or wherever the workspace
dropdown renders):

Problem: dropdown opens but shows only a checkmark, no workspace name visible.

Investigate:
1. Check what data the workspace selector receives — does workspace.store
   have the workspace name populated?
2. The workspace object likely has: { id, name, slug, ... }
   The dropdown item must render workspace.name not workspace.slug
3. Check if the dropdown is trying to render before workspace.store.loadWorkspaces()
   completes — add a loading state
4. Fix: ensure workspace name displays in each dropdown item
5. Fix: on app load, set currentWorkspace to the first workspace in the list
   if no workspace is currently selected (auto-select logic)
6. Fix: the workspace name should appear in the selector trigger button,
   not just in the dropdown items

--- FIX 2: Dashboard workspace name missing ---

In web/src/views/dashboard/DashboardView.vue:
"Here's what's happening in ." — the workspace name is empty.
Fix: use workspace.store.currentWorkspace?.name in the subtitle.
Show "your workspace" as fallback if name not yet loaded.

--- FIX 3: Enable all 6 workflow creation methods (Phase 3 is complete) ---

In web/src/views/workflow/WorkflowListView.vue (or NewWorkflowView.vue):
All 6 creation method cards currently show as disabled/grayed with
"Coming next" or "Phase 3" badges.

Phase 3 is complete. Enable all 6:
- Plain English → navigates to /workflows/new?method=nl
- Voice → navigates to /workflows/new?method=voice
- Whiteboard photo → navigates to /workflows/new?method=visual
- Slack conversation → navigates to /workflows/new?method=conversation
- Upload a document → navigates to /workflows/new?method=document
- Drag & drop → navigates to /workflows/new (opens canvas directly)

Remove all "Coming next", "Phase 3", "Phase 4" badges from creation method cards.
Make all 6 cards clickable with hover state (teal border on hover).

Also fix the same in DashboardView.vue — the 6 creation method icons on the
dashboard should also be enabled and clickable.

--- FIX 4: Weather page 404 (Image 6) ---

The frontend is calling the wrong URL for weather data.
Check web/src/services/workflow.service.ts or weather.service.ts:
The endpoint should be:
  GET /api/v1/workspaces/{workspaceId}/weather

Common issue: workspaceId is undefined when the call is made because
workspace.store.currentWorkspace is not yet loaded.

Fix:
1. In WorkflowWeatherView.vue, wait for currentWorkspace to be available
   before making the API call
2. Add a watch on currentWorkspace — reload weather data when it changes
3. If workspaceId is null/undefined, show a loading state rather than
   making the API call (which returns 404 because the URL becomes
   /api/v1/workspaces/undefined/weather)

--- FIX 5: Improve Workflows empty state UI ---

In web/src/views/workflow/WorkflowListView.vue:
The empty state creation method icons are too small and not actionable.

Replace the small icon row with a proper empty state using FmEmptyState:
- Icon: workflow icon (large, teal)
- Title: "Create your first workflow"
- Description: "Choose how you want to build — describe it in plain English,
  upload a document, or draw it on the canvas."
- Show 6 creation method cards below (same as dashboard) — properly sized,
  all enabled, with hover states
- Each card: 120px wide, icon + label, teal border on hover, clickable

After all fixes:
npm run build --prefix web — 0 TypeScript errors

git add . && git commit -m "fix(ui): workspace selector, creation methods enabled, weather 404, empty states" && git push origin develop