Read CLAUDE.md.
Fix the following 5 UI issues found during live testing.

--- FIX 1: Workspace selector still empty after proxy change ---

The nginx proxy change moved API calls to relative URLs. The workspace store
loadWorkspaces() call may now be using the wrong base URL or failing silently.

In web/src/stores/workspace.store.ts:
1. Add error logging to loadWorkspaces() so failures are visible in console
2. Ensure the API call uses the relative URL /api/v1/workspaces (no base URL prefix)
3. In the workspace selector component (TopBar.vue or WorkspaceSwitcher.vue):
   - Show ALL workspaces from allWorkspaces array
   - Auto-select the first workspace on load if none is selected
   - Display workspace.name in the trigger button (not just in dropdown items)
   - If allWorkspaces is empty, show "Loading..." not a blank dropdown

--- FIX 2: Getting started checklist — remove phase badges, enable first item ---

In web/src/views/dashboard/DashboardView.vue:
The getting started checklist has 4 items with phase badges:
- "Create your first workflow" — badge says "Coming next" → REMOVE badge, make clickable, link to /workflows/new
- "Connect a system" — badge says "Phase 4" → REMOVE badge, make clickable, link to /library
- "Trigger your first run" — badge says "Phase 3" → REMOVE badge, make clickable, link to /instances
- "Invite a team member" — already has "Start →" link → keep as is

All phases are complete. Remove all phase/coming-next badges entirely.
Each checklist item: clicking anywhere on the row navigates to the linked page.

--- FIX 3: NL template form — white input fields, dark text ---

In web/src/components/creation/NlTemplateForm.vue:
All textarea and input elements have dark background (#1e293b or similar).
This makes text invisible against the dark background.

Fix all form fields:
- Background: white (#FFFFFF)
- Text color: #111827 (near black)
- Border: #E5E7EB (light gray)
- Placeholder text: #9CA3AF (gray)
- Focus border: #1D9E75 (teal)
- Label text: #374151 (dark gray)
- The "Generate Workflow" button: #1D9E75 teal background, white text

Apply using Tailwind classes:
textarea: bg-white text-gray-900 border border-gray-300 placeholder-gray-400
  focus:outline-none focus:ring-2 focus:ring-teal-500 focus:border-teal-500
  rounded-lg p-3 w-full

--- FIX 4: "Drag & drop" goes directly to canvas, skip method selector ---

In web/src/views/dashboard/DashboardView.vue and
web/src/views/workflow/WorkflowListView.vue:

When user clicks the "Drag & drop" creation method card:
- Do NOT navigate to /workflows/new (the method selector page)
- Navigate DIRECTLY to /workflows/new?method=canvas
  OR navigate directly to the SFG canvas editor

In web/src/views/workflow/NewWorkflowView.vue:
- When route.query.method === 'canvas', skip the method selector entirely
  and immediately show the canvas editor (SfgCanvas.vue)
- Do not show the creation method cards when method=canvas is in the URL

--- FIX 5: Add padding to content pages ---

In web/src/components/layout/AppShell.vue or the main content area wrapper:
The content area needs consistent padding so it does not touch the sidebar
or the right edge.

Add to the main content area div:
class="... px-8 py-6" (or equivalent)

Specifically fix these pages by adding padding to their root container:
- web/src/views/workflow/WorkflowListView.vue — add px-8 py-6 to root div
- web/src/views/instance/InstanceListView.vue — add px-8 py-6 to root div
- web/src/views/library/LibraryView.vue — add px-8 py-6 to root div
- web/src/views/weather/WorkflowWeatherView.vue — add px-8 py-6 to root div

Better approach: add padding to the AppShell content slot wrapper once
so ALL pages get it automatically without touching each view file:
In AppShell.vue, find the main content div and add:
class="... p-8" to the slot container

After all fixes:
npm run build --prefix web — 0 TypeScript errors

git add . && git commit -m "fix(ui): workspace selector, checklist badges, NL form colors, canvas direct nav, content padding" && git push origin develop