---
prompt-id: 08-help-content-docs
phase: 01
sequence: 8
roles: [Executor, Verifier, UX]
type: feature
depends-on: [07-docker-deployment]
estimated-complexity: Medium
---

# Help System Content + docs.flowamaz.io Structure

## Context
Help panel infrastructure built in prompt 06. Now write all Phase 1 content,
wire contextual routing for all Phase 1 screens, and scaffold docs.flowamaz.io.
See FUNCTIONAL.md §10 for complete help system specification and content map.

## Objective
Write all 15 Phase 1 help articles (complete — not placeholder text), scaffold
docs.flowamaz.io with Docusaurus, wire all Phase 1 error states to help articles,
and verify contextual routing works on all Phase 1 routes.

## Scope

### What to Build

**15 help articles — written complete and useful (not Lorem ipsum):**

Every article must include:
- Title (H1)
- Last updated date: 2026-05-24
- Reading time estimate: "N min read"
- Plan badge: one of [All plans · Cloud only · Enterprise]
- Body content (see spec below)
- Footer: "Was this helpful? 👍 👎" + "Edit on GitHub ↗" link

---

**Getting Started (6 articles):**

`getting-started/what-is-flowamaz.md`
Content: What Flowamaz does (plain English), who it's for (business teams + engineers),
key concepts defined (workflow, instance, node, connector, workspace, environment,
credential, human gate), the 3 editions (Community/Cloud/Enterprise) with one-liner each,
5-minute visual overview of the creation-to-execution journey.

`getting-started/install-community-edition.md`
Content: Prerequisites (Docker OR Node 20+), three install methods:
1. Docker Compose (one command: curl -fsSL https://get.flowamaz.io/community | sh)
2. Homebrew (brew install flowamaz/tap/fmz && fmz start)
3. Manual / VPS (download binary, systemd service example)
After install: open http://localhost:3000, create org, first login.
Troubleshooting: port already in use, Docker not running.

`getting-started/cloud-signup.md`
Content: Go to app.flowamaz.io, click "Start free trial", fill registration form
(org name, URL, email, password), pick a plan, complete onboarding wizard, create
first workspace. Includes what the 14-day trial includes and what happens after trial.

`getting-started/plans-comparison.md`
Content: Full comparison table (Community/Starter/Pro/Enterprise) with every limit
from FUNCTIONAL.md §13.2. Plain English explanation of what each limit means in practice.
"When to upgrade" guidance: "If you hit 500 runs in a month, upgrade to Starter."
No artificial urgency.

`getting-started/migrate-community-to-cloud.md`
Content: When to migrate (hitting limits, need SSO, need team), how:
1. fmz cloud migrate --workspace ws_name
2. What carries over (workflows, run history, Git repos)
3. What to reconfigure (credentials — re-enter for security)
4. Estimated time: < 5 minutes for typical workspace

`getting-started/key-concepts-glossary.md`
Content: Alphabetical glossary. Each term: name, one-sentence definition, example.
Terms: Workflow, Instance, Node (all 6 types), Connector, Workspace, Organisation,
Environment, Credential, Gate, Human Gate, SFG (Simple Flow Graph), YAML, Co-pilot,
Library, Marketplace, Durable execution, Exactly-once.

---

**Workspaces (5 articles):**

`workspaces/what-is-a-workspace.md`
Content: Workspace as isolation boundary, how it fits in hierarchy
(Platform → Organisation → Workspace → Environment), workspace limits by plan (table),
environments explained (dev/staging/production), when to create multiple workspaces
(one per team, one per project).

`workspaces/invite-team-members.md`
Content: Step by step — go to Settings → Members → Invite, enter email, pick role.
What happens if email not found (user must register first — no email invitation in Phase 1).
How to change a role. How to remove a member. What "inactive" member means.

`workspaces/roles-and-permissions.md`
Content: All 5 roles explained in plain English with examples of what each can do.
Permission table (from FUNCTIONAL.md §4.5). "Who can do what" scenario table:
"Can a Designer publish to production? No — requires Admin."

`workspaces/api-keys.md`
Content: What API keys are for (programmatic access, CI/CD, external systems),
how to create one (Settings → API Keys → Create), scopes explained with examples,
fmz_live_ vs fmz_test_ format, security best practices (never commit, rotate regularly),
how to revoke.

`workspaces/environments.md`
Content: Dev/Staging/Production explained, what changes between environments
(connector credentials scoped per environment — coming in Phase 4), how to promote
a workflow from dev to staging to production using Git (Phase 5 preview).

---

**Plans & Billing (4 articles):**

`billing/community-vs-cloud.md`
Content: What Community includes (full engine, 5 workflows, 500 runs, 1 user,
Claude Haiku BYOK), what Cloud adds (teams, unlimited workflows, all AI providers,
SSO, support). Honest comparison — no FUD about Community. "Community is genuinely
useful for individual developers and evaluation."

`billing/upgrade-guide.md`
Content: How to upgrade (Settings → Billing → Upgrade), what happens immediately
(limits increase, features unlock), proration explained plainly, payment methods
(credit card), what happens if payment fails (grace period, then suspension).

`billing/understanding-your-invoice.md`
Content: Each line item explained: workflow runs (what counts as a run), members
(peak count in period), storage (snapshot at month end), AI calls (only platform-managed
calls — BYOK not billed). How to track usage (Usage tab in Settings). Overage caps.
How to export invoice as PDF.

`billing/enterprise-self-hosted.md`
Content: Who it's for (regulated industries, data sovereignty requirements),
what's included (all Pro features + CMEK + data residency + dedicated support),
deployment requirements (Docker, min specs), how to get a quote (email team@flowamaz.io),
licence key system (annual renewal).

---

**Docusaurus site scaffold (docs/ directory at repo root):**

```
docs/
  docusaurus.config.js
  sidebars.js
  package.json
  docs/
    getting-started/
      what-is-flowamaz.md
      install-community-edition.md
      cloud-signup.md
      plans-comparison.md
      migrate-community-to-cloud.md
      key-concepts-glossary.md
    workspaces/
      what-is-a-workspace.md
      invite-team-members.md
      roles-and-permissions.md
      api-keys.md
      environments.md
    billing/
      community-vs-cloud.md
      upgrade-guide.md
      understanding-your-invoice.md
      enterprise-self-hosted.md
  static/
    img/
      flowamaz-logo.svg
  README.md
```

docusaurus.config.js:
- Site title: Flowamaz Docs
- URL: https://docs.flowamaz.io
- tagline: "AI-native workflow orchestration"
- GitHub edit URL: https://github.com/flowamaz-io/docs/blob/main/docs/
- Navbar: logo, links to app.flowamaz.io, GitHub org
- Footer: links to flowamaz.com, GitHub, security email

**Error → article routing (update FmErrorState component):**
```ts
export const errorArticleMap: Record<number, string> = {
  401: 'getting-started/cloud-signup',
  403: 'workspaces/roles-and-permissions',
  404: 'getting-started/key-concepts-glossary',
  429: 'billing/community-vs-cloud',
}
// API-specific errors:
'workspace_not_found': 'workspaces/what-is-a-workspace'
'api_key_invalid': 'workspaces/api-keys'
'plan_limit_exceeded': 'billing/upgrade-guide'
```

**Verify all Phase 1 routes have help context:**
```ts
// articleMap.ts — verify these all exist and map to real articles
'/login'              → 'getting-started/cloud-signup'
'/register'           → 'getting-started/cloud-signup'
'/onboarding'         → 'getting-started/what-is-flowamaz'
'/'                   → 'getting-started/what-is-flowamaz'
'/settings'           → 'workspaces/what-is-a-workspace'
'/settings/members'   → 'workspaces/invite-team-members'
'/settings/api-keys'  → 'workspaces/api-keys'
```

### What NOT to Build
- No Phase 2+ articles yet
- No AI-powered help search — Phase 4
- Do not write placeholder content — every article must be complete and usable

## Technical Requirements
- [ ] All 15 articles exist in web/src/help/articles/ as .md files
- [ ] All 15 articles mirrored identically in docs/docs/
- [ ] Every article has: title, updated date, reading time, plan badge, footer
- [ ] No article uses placeholder text ("Lorem ipsum", "Coming soon", "TBD")
- [ ] Docusaurus config valid — runs with npx docusaurus start without errors
- [ ] Help panel search: "invite" → invite-team-members article appears
- [ ] Help panel search: "free plan" → community-vs-cloud article appears
- [ ] Help panel search: "install" → install-community-edition appears
- [ ] Every FmErrorState in Phase 1 UI has a help article link configured
- [ ] articleMap.ts covers all 7 Phase 1 routes with correct article paths

## Acceptance Criteria
- [ ] All 15 articles exist with complete content
- [ ] Docusaurus: npx docusaurus start — no errors, all 15 articles render
- [ ] Help panel: search "roles" → roles-and-permissions article found
- [ ] articleMap.ts: /settings/members → opens correct article in panel
- [ ] FmErrorState on 403 → shows link to roles-and-permissions article
- [ ] Article footer links: "Edit on GitHub ↗" points to correct URL

## Output Expected
```
web/src/help/articles/getting-started/what-is-flowamaz.md
web/src/help/articles/getting-started/install-community-edition.md
web/src/help/articles/getting-started/cloud-signup.md
web/src/help/articles/getting-started/plans-comparison.md
web/src/help/articles/getting-started/migrate-community-to-cloud.md
web/src/help/articles/getting-started/key-concepts-glossary.md
web/src/help/articles/workspaces/what-is-a-workspace.md
web/src/help/articles/workspaces/invite-team-members.md
web/src/help/articles/workspaces/roles-and-permissions.md
web/src/help/articles/workspaces/api-keys.md
web/src/help/articles/workspaces/environments.md
web/src/help/articles/billing/community-vs-cloud.md
web/src/help/articles/billing/upgrade-guide.md
web/src/help/articles/billing/understanding-your-invoice.md
web/src/help/articles/billing/enterprise-self-hosted.md
web/src/help/articleMap.ts (updated with all routes + error map)
docs/docusaurus.config.js
docs/sidebars.js
docs/package.json
docs/docs/**/*.md (all 15 mirrored articles)
docs/README.md
```

## Notes for Executor
- Help articles are loaded at app startup into a Map<string, string> in useHelp composable
  Use import.meta.glob to load all markdown files at build time — Vite supports this.
- Reading time estimate: 1 min per ~200 words — calculate and add to each article frontmatter
- Plan badge: add to article frontmatter: plans: "All plans" | "Cloud only" | "Enterprise"
  Render as a coloured badge in ArticleRenderer
- Docusaurus version: use latest stable (3.x). Use @docusaurus/preset-classic.
- Edit on GitHub URL format: https://github.com/flowamaz-io/docs/blob/main/docs/{article-path}.md
