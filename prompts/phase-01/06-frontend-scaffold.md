---
prompt-id: 06-frontend-scaffold
phase: 01
sequence: 6
roles: [Executor, Verifier, UI, UX]
type: feature
depends-on: [05-workspace-management-api]
estimated-complexity: High
---

# Frontend Scaffold — Vue 3, Auth, Workspace Shell, Help System, Onboarding

## Context
Backend API is complete. Build the Vue 3 frontend. Quality bar: every screen must
feel like it belongs in Vercel, Linear, or Notion. Zero style blocks. TypeScript strict.
Read FUNCTIONAL.md §10 (help system 7 layers) and §19 (design principles) before starting.

## Objective
Complete Vue 3 frontend: auth views, app shell, workspace dashboard, help panel
(all 7 layer infrastructure), onboarding wizard, getting-started checklist, and all
base components to production quality.

## Scope

### What to Build

**Project structure:**
```
web/
  src/
    components/
      common/           ← Fm* base components
      layout/           ← AppShell, Sidebar, TopBar
      help/             ← HelpPanel, HelpTooltip, HelpSearch
      onboarding/       ← OnboardingWizard, GettingStartedChecklist
    views/
      auth/             ← LoginView, RegisterView
      dashboard/        ← DashboardView
      workspace/        ← WorkspaceSettingsView, MembersView, ApiKeysView
      onboarding/       ← OnboardingView
    composables/        ← useAuth, useWorkspace, useHelp, useToast
    stores/             ← auth.store, workspace.store, ui.store
    services/           ← api.service, auth.service, workspace.service
    router/             ← index.ts with route guards
    types/              ← all TypeScript interfaces
    utils/              ← date, string, error helpers
    help/
      articles/         ← all 15 Phase 1 help articles as markdown
      articleMap.ts     ← route → article mapping
  e2e/                  ← Playwright tests (prompt 09)
  public/
  index.html
  vite.config.ts
  tailwind.config.ts
  tsconfig.json
  package.json
```

**Tailwind brand tokens (tailwind.config.ts):**
```js
colors: {
  primary: { DEFAULT: '#1D9E75', ... },    // teal — brand primary
  accent:  { DEFAULT: '#534AB7', ... },    // purple — accent/AI
  amber:   { DEFAULT: '#EF9F27', ... },    // amber — human gates, warnings
  danger:  { DEFAULT: '#E24B4A', ... },    // red — errors, destructive
  // extend grays using slate
}
```

**Base components (Flowamaz.Core/Components — all zero style blocks):**

FmButton — variant (primary/secondary/danger/ghost), size (sm/md/lg),
  loading (spinner replaces label, disabled), icon slot (left/right)

FmInput — label, placeholder, hint text, error text (red, below field),
  ? icon (HelpTooltip trigger), disabled state, loading state

FmBadge — role colours: Admin=purple, Designer=teal, Operator=amber,
  Runner=blue, Viewer=gray. Status colours: active=green, inactive=gray

FmModal — portal (teleport to body), backdrop click closes, Escape key closes,
  header slot, body slot, footer slot, size (sm/md/lg)

FmAlert — type (success/error/warning/info), dismissible option, icon, message

FmSpinner — size (sm/md/lg), color inherits from parent

FmDropdown — trigger slot, items slot, position (bottom-left/bottom-right),
  keyboard navigation (arrow keys, Enter, Escape)

FmEmptyState — icon prop, title, description, CTA button props.
  NEVER renders "No results" without a call-to-action.
  Every empty state explains what to do next.

FmErrorState — title, description (what happened + why), action button,
  help article link. NEVER renders "Something went wrong" without next steps.

FmToast — global toast system via provide/inject. Types: success/error/info/warning.
  Auto-dismiss 5s. Max 3 visible at once.

FmPagination — page, pageSize, total, totalPages props. Emits: page-change.

**Auth views:**

LoginView (/login):
- Flowamaz logo top centre
- "Sign in to your organisation" heading
- Fields: Email, Organisation URL (org_slug), Password
- "Forgot password?" link (shows "coming soon" toast for now)
- "Create organisation" link → /register
- Submit → POST /api/v1/auth/login
- Error: display above form, actionable message (not generic)
- Loading: button spinner during request
- After login: redirect to /onboarding (if onboarding_completed=false) else /

RegisterView (/register):
- Flowamaz logo + "Start your 14-day free trial"
- Plan selector: Community (free) / Starter ($49/mo) / Pro ($199/mo) — cards, not dropdown
- Fields: Organisation name, Organisation URL (auto-generated from name, editable),
  Full name, Email, Password (with strength indicator)
- Slug: auto-slugify from org name on keyup, user can override
- Submit → POST /api/v1/auth/register
- Error: field-level validation + server errors shown clearly
- Success: redirect to /onboarding

**Onboarding wizard (OnboardingView /onboarding):**

Step 1 — "Name your first workspace"
- Input: workspace name (auto-generates slug)
- Creates workspace via POST /api/v1/workspaces
- Next button → step 2

Step 2 — "Your workflows will build themselves"
- Shows the 6 creation method icons with labels (text, voice, whiteboard,
  conversation, document, canvas) in a preview grid
- Each has a short label: "Plain English", "Voice", "Whiteboard photo",
  "Slack conversation", "Upload a document", "Drag & drop"
- Subtitle: "These are all available once we finish setting up"
- Next button → step 3

Step 3 — "Invite your first team member" (optional)
- Email input + role selector (Designer/Operator/Viewer)
- Skip button (prominent — inviting is optional)
- If email provided: POST /api/v1/workspaces/{id}/members
- Finish → marks onboarding complete in localStorage + navigates to /

Wizard state: stored in localStorage key "fmz_onboarding_{orgId}".
Shown only once. If user closes/refreshes midway, resumes from last step.

**Dashboard (/):**

Getting-started checklist (shown when !onboarding.allComplete):
```
□ Create your first workflow     → links to /workflows/new (Phase 3 — grayed out with "coming next")
□ Connect a system               → links to /library (Phase 4 — grayed out)
□ Trigger your first run         → grayed out (Phase 3)
□ Invite a team member           → links to /settings/members
```
Each item: checkbox (completed = green checkmark), title, description, action link.
Dismiss button: "I'll explore on my own" (hides permanently per org in localStorage).
Progress: "2 of 4 complete" shown above the checklist.

4 metric cards (Phase 1 — all show "--" with label):
- Total workflows, Active instances, Runs this month, Team members

"Create your first workflow" hero section:
- Heading: "What would you like to automate?"
- 6 creation method cards (same as onboarding step 2) — all disabled/grayed in Phase 1
- Subtitle: "Workflow creation unlocks in Phase 2 — engine is being built"
- This placeholder teaches users what's coming and sets expectations

**Workspace views:**

WorkspaceSettingsView (/settings):
- Workspace name (editable), slug (read-only after creation)
- AI model configuration: per-function dropdown showing [provider] [model]
  - 7 rows (F1-F7), each with provider select + model select
  - Shows "Resolved from: workspace / org / platform" badge
  - Models filtered to org's AllowedAiProviders
  - GET /api/v1/workspaces/{id}/ai-config populates this
  - PUT saves changes
- Run retention (dropdown: 7/30/90/180/365 days — limited by plan)
- Save button with loading state

MembersView (/settings/members):
- Members table: avatar initial, name, email, role badge, joined date, actions
- Invite modal: email input + role dropdown (Designer/Operator/Runner/Viewer)
- Role change: inline dropdown in table row (disabled for own row)
- Remove: confirmation modal before API call
- Empty state: FmEmptyState with "Invite your first team member" CTA

ApiKeysView (/settings/api-keys):
- Keys table: name, prefix (fmz_live_xxx...), scopes badges, last used, status
- Create modal: name + environment selector + scopes multi-select
- After create: FmAlert with PlainKey displayed ONCE, copy button,
  warning "This will not be shown again"
- Revoke: confirmation modal

**Help Panel (all 7 layer infrastructure from FUNCTIONAL.md §10):**

HelpPanel.vue:
- Trigger: ? button in TopBar, Shift+? keyboard shortcut (global)
- Slides in from right (300px wide, overlay, backdrop-less)
- Close: click X, Escape key, clicking back in main area
- Sections: navigation tree (collapsible), search bar, article content area
- Default: opens to article for current route (from articleMap.ts)
- Navigation tree: mirrors content map from FUNCTIONAL.md §10.9

HelpSearch.vue:
- Real-time search across article titles and content (client-side — articles loaded at startup)
- Debounce 300ms
- Shows: title, section path, snippet with query highlighted
- "No results" → FmEmptyState with "Try different keywords" message

HelpTooltip.vue:
- Wraps any element, shows ? icon on hover of parent
- Click ? → shows tooltip (not help panel) with field description + "Learn more →" link
- "Learn more →" → opens HelpPanel to the linked article
- Used on every form field in the application

ArticleRenderer.vue:
- Renders markdown to HTML (use marked.js)
- Applies Tailwind prose classes
- Article footer: "Was this helpful? 👍 👎" + "Edit on GitHub ↗" link
  Edit on GitHub link: https://github.com/flowamaz-io/docs/blob/main/docs/[article-path].md

articleMap.ts — maps route path to help article filename:
```ts
export const articleMap: Record<string, string> = {
  '/login': 'getting-started/cloud-signup',
  '/register': 'getting-started/cloud-signup',
  '/onboarding': 'getting-started/what-is-flowamaz',
  '/': 'getting-started/what-is-flowamaz',
  '/settings': 'workspaces/what-is-a-workspace',
  '/settings/members': 'workspaces/invite-team-members',
  '/settings/api-keys': 'workspaces/api-keys',
  // Phase 3+ routes added in later prompts
}
```

**Pinia stores:**

auth.store.ts:
- State: accessToken (string | null), user (UserInfo | null), isAuthenticated
- Actions: login(credentials), register(data), logout, refreshToken, loadFromCookie
- Access token: stored in store memory only (NOT localStorage — XSS protection)
- On app init: attempt silent refresh to restore session

workspace.store.ts:
- State: currentWorkspace, allWorkspaces, members, apiKeys, aiConfig
- Actions: loadWorkspaces, switchWorkspace(id), loadMembers, inviteMember,
  updateMemberRole, removeMember, loadApiKeys, createApiKey, revokeApiKey,
  loadAiConfig, updateAiConfig

ui.store.ts:
- State: helpPanelOpen, helpPanelArticle, sidebarCollapsed, toasts[]
- Actions: openHelp(article?), closeHelp, toggleSidebar, addToast, dismissToast

**API service layer:**

api.service.ts:
- Axios instance with baseURL from VITE_API_BASE_URL
- Request interceptor: adds Authorization: Bearer {token} from auth.store
- Response interceptor: on 401 → attempt token refresh → retry original request
  If refresh fails → logout + redirect to /login
- Error normaliser: maps API errors to UserFacingError { message, field?, helpArticle? }

**Router guards:**
- requiresAuth: redirect to /login if !isAuthenticated
- requiresOnboarding: redirect to /onboarding if onboarding not complete AND not on /onboarding
- alreadyAuthenticated: redirect to / if already logged in (for /login, /register)

**Package.json scripts:**
```json
{
  "dev": "vite",
  "build": "vue-tsc && vite build",
  "test": "vitest run",
  "test:watch": "vitest",
  "e2e": "playwright test",
  "lint": "eslint src --ext .ts,.vue --fix",
  "typecheck": "vue-tsc --noEmit"
}
```

### What NOT to Build
- No SFG canvas — Phase 3
- No YAML editor — Phase 3
- No workflow list — Phase 3
- No Library section — Phase 4
- Metric cards on dashboard show "--" placeholders

## Technical Requirements
- [ ] TypeScript strict — zero any types anywhere
- [ ] Zero style blocks in any .vue file — all Tailwind classes only
- [ ] Composition API only — no Options API
- [ ] Access token in Pinia memory ONLY — never localStorage
- [ ] 401 → auto-refresh → retry exactly once (not infinite loop)
- [ ] Every form: loading state on submit, validation before submit, server error display
- [ ] Every error state uses FmErrorState (not bare text)
- [ ] Every empty state uses FmEmptyState with CTA (not "no results")
- [ ] Help panel: opens to correct article for every Phase 1 route
- [ ] Shift+? works globally (event listener in AppShell mounted hook)
- [ ] Onboarding wizard resumes from last step on refresh
- [ ] Mobile responsive: sidebar collapses at < 768px, hamburger shows
- [ ] npm run build — zero TypeScript errors, zero ESLint errors
- [ ] Vitest setup with MSW for API mocking
- [ ] Unit tests: auth.store (login/logout/refresh), WorkspaceSelector, MembersView

## Acceptance Criteria
- [ ] npm run build — clean build, no errors
- [ ] Login → dashboard → workspace selector works end to end against real API
- [ ] Onboarding wizard: completes, persists state on refresh, marks complete
- [ ] Getting-started checklist: visible on dashboard, checkable, dismissable
- [ ] Help panel: Shift+? opens, routes to correct article for /settings
- [ ] HelpTooltip ? icon visible on Email field in login form
- [ ] Members invite modal: opens, submits, shows new member in list
- [ ] API key create: PlainKey shown once in alert, copy button works
- [ ] AI config: dropdowns populated from /ai-config endpoint

## Output Expected
```
web/src/ (complete structure as described above)
web/src/help/articles/*.md (all 15 Phase 1 articles — written in prompt 08)
web/src/help/articleMap.ts
web/vite.config.ts
web/tailwind.config.ts
web/tsconfig.json
web/package.json
web/src/tests/ (Vitest unit tests)
```

## Notes for Executor
- marked.js for markdown rendering: import { marked } from 'marked'
- HelpTooltip position: use @floating-ui/vue for tooltip positioning
- Tailwind v4: use @import 'tailwindcss' in main CSS — no config file needed for basic
- Do NOT use <style> blocks — use Tailwind classes exclusively
- FmEmptyState and FmErrorState are the most important components — they appear
  everywhere. Get them right before building views.
- Access token memory-only: when user opens a new tab, they must re-authenticate
  OR implement a BroadcastChannel to sync token across tabs (recommended)
- Register form slug: slugify = input.toLowerCase().replace(/[^a-z0-9]/g, '-').replace(/-+/g, '-')
