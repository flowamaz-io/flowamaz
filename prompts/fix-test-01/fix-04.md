Read CLAUDE.md.
Fix three issues found during live testing of the canvas and NL workflow creation form.

--- FIX 1: Canvas light theme ---

In web/src/components/canvas/SfgCanvas.vue:
The canvas background is currently black (#000000 or near-black).
Change to a light theme:

Canvas background: #F8FAFC (very light blue-gray — professional, easy on eyes)
Canvas grid/dots: #E2E8F0 (subtle light gray grid)
Node palette sidebar: #F1F5F9 background, #374151 text
Canvas toolbar: white background, subtle border-bottom: 1px solid #E5E7EB

In the Cytoscape.js style configuration, update:
- Core background: '#F8FAFC'
- Node label color: '#111827' (dark, readable on light background)
- Edge color: '#94A3B8'
- Selected node border: '#1D9E75' teal

The node type colours stay the same (purple trigger, teal action, etc)
but their label text must be white (nodes are coloured, labels on nodes are white).

Also: the minimap in bottom-right corner — give it a white background
with a subtle border so it stands out from the canvas.

--- FIX 2: 401 on workflow generation — fix API URL ---

The NL generation call is using an absolute URL with localhost:8443.
After the nginx proxy change, all API calls must use relative URLs.

In web/src/services/creation.service.ts (or wherever generateWorkflow is called):
Find any absolute URL like:
  `${import.meta.env.VITE_API_BASE_URL}/api/v1/workspaces/...`
  or `http://localhost:8307/api/v1/...`
  or `https://localhost:8443/api/v1/...`

Replace with relative URL:
  `/api/v1/workspaces/${workspaceId}/workflows/generate`

Check ALL methods in creation.service.ts and any other service files
that might still have absolute URLs. The pattern to find and fix:
  grep -r "localhost" web/src/services/ — must return 0 results after fix

Also check web/src/stores/ for any hardcoded localhost URLs.

The Axios instance in api.service.ts already uses baseURL = '' (empty/relative).
All service calls must use paths starting with /api/v1/ not http://localhost.

--- FIX 3: Tooltip hints on NL form fields ---

In web/src/components/creation/NlTemplateForm.vue:
Add helper text below each field label that shows while the user is typing.
This is NOT a tooltip — it is persistent hint text always visible below the label.

For each field, add a <p> tag between the label and the textarea:
  class="text-sm text-gray-500 mb-2"

Field hints:
- Workflow Name: no hint needed (placeholder is sufficient)
- Purpose: "What does this workflow do and who uses it? e.g. 'Routes purchase requests from employees to their manager for approval before submitting to finance.'"
- Trigger: "What starts this workflow? e.g. 'A form submission with fields: requester name, amount, description, and cost centre.'"
- Steps: "Describe each step in order, including decisions and who does what. e.g. '1. Validate the requester is an active employee. 2. If amount > RM5000, route to manager approval. 3. Manager approves or rejects. 4. If approved, create PO in system.'"
- Rules & Constraints: "SLA deadlines, approval thresholds, retry rules. e.g. 'Manager must respond within 48 hours. If no response, escalate to department head. Retry failed steps 3 times.'"
- Systems & AI: "Which external systems are involved? Any AI processing needed? e.g. 'SAP for PO creation, Slack for notifications, no AI needed.'"
- Existing Context: "Optional. Paste a related email, ticket, or document excerpt to give the AI more context."

Keep placeholder text as it is (shows when field is empty).
The hint text shows ALWAYS — above the textarea, below the label.
Use text-sm text-gray-500 so it is clearly secondary to the label.

After all fixes:
npm run build --prefix web — 0 TypeScript errors

git add . && git commit -m "fix(canvas): light theme; fix(creation): relative API URLs; feat(nl-form): field hints" && git push origin develop