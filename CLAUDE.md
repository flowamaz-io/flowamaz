# Flowamaz — CLAUDE.md
# Project Constitution — Claude Code reads this first on every session

Read FUNCTIONAL.md next. It is the complete requirements bible.
This file is the execution constitution. FUNCTIONAL.md is the product spec.
Both must be read before any prompt is executed.

---

## Project Overview

- **Name**: Flowamaz
- **Domains**: flowamaz.com (marketing) · flowamaz.io (technical infrastructure) · flowamaze.com (redirect)
- **Email**: team@flowamaz.io (Microsoft 365 Business Basic) · noreply@flowamaz.io (Resend transactional)
- **GitHub org**: flowamaz-io (standalone — NOT under Tootker Solutions)
- **Description**: AI-native workflow orchestration platform. Users create workflows from plain English, whiteboard photos, conversations, or by drawing on the canvas. Durable execution, multi-model AI, Git-native versioning, connector marketplace.
- **Phase Count**: 6 feature phases
- **Current Phase**: 01

---

## Git Repository Configuration

### Remote URLs
```
Main repo (public):     https://github.com/flowamaz-io/flowamaz.git
Platform repo (private):https://github.com/flowamaz-io/platform.git
Connectors repo:        https://github.com/flowamaz-io/connectors.git
Templates repo:         https://github.com/flowamaz-io/templates.git
Docs repo:              https://github.com/flowamaz-io/docs.git
Infra repo (private):   https://github.com/flowamaz-io/infra.git
```

### Git Initialisation (first session only)
On the very first session before executing any prompt, PM Agent must:
```bash
git init
git remote add origin https://github.com/flowamaz-io/flowamaz.git
git checkout -b develop
git add .
git commit -m "chore: initial project scaffold — Flowamaz ClaudeCode Foundation"
git push -u origin develop
```

### Commit Convention (every prompt)
After every prompt completes and before /clear:
```bash
git add .
git commit -m "feat(scope): description [prompt-id]"
git push origin develop
```

Commit types: feat, fix, chore, docs, test, refactor, security
Scopes: api, core, infra, web, auth, workspace, ai, docker, docs, help

### Branch Strategy
- `develop` — all Phase 1 work lands here
- `main` — only touched at phase end (release branch → main)
- Never commit directly to main

### Git Author (from environment)
```bash
git config user.name "Flowamaz Bot"
git config user.email "team@flowamaz.io"
```

---



- Backend: YES (.NET)
- Web: YES (Vue 3)
- Mobile: NO (Year 2 — Flutter)

---

## Foundation Reference Documents

Agents read these before executing any prompt. They are the Foundation standards.

```
foundation-docs/
  tech-stack.md          — Standard stack for all projects (Foundation defaults)
  serilog-standard.md    — Logging rules Executor must follow
  rbac-pattern.md        — Base auth pattern
  verifier-standards.md  — Enterprise grade checklist (Verifier reads this)
  security-standards.md  — Security scan checklist (Security Agent reads this)

templates/
  prompt-template.md     — Format for all prompt files
  phase-report-template.md — Phase report structure PM must follow exactly
```

Note: Flowamaz-specific overrides to Foundation defaults are declared below.
Where this CLAUDE.md conflicts with foundation-docs/, this CLAUDE.md wins.

---

## Tech Stack

### Backend (.NET)
- Latest stable .NET, no preview
- Clean architecture: Flowamaz.Api / Flowamaz.Core / Flowamaz.Application / Flowamaz.Infrastructure
- EF Core code-first, lazy loading, PostgreSQL, snake_case naming
- Redis via StackExchange.Redis (task queue, session cache, semantic cache, rate limiting)
- Serilog structured JSON (entry/exit/error on every function — no exceptions)
- AutoWrapper on all endpoints
- FluentValidation on all request DTOs
- Scalar at /scalar — always public, no auth
- Custom JWT (not ASP.NET Identity)
- Quartz.NET (timer scanner, scheduled triggers)
- Hangfire (webhook delivery, metering events)
- YamlDotNet (all YAML parsing/generation — never raw string manipulation)
- LibGit2Sharp (Git operations — always via WorkspaceGitService, never direct)
- Anthropic.SDK + provider-agnostic IAiService
- NJsonSchema (JSON Schema validation for output schemas)

### Frontend (Vue 3)
- TypeScript strict — no `any` types
- Composition API only — no Options API
- Tailwind CSS v4 — zero style blocks ever, no exceptions
- Pinia, Vue Router 4, Axios, VeeValidate + Zod
- Cytoscape.js + cytoscape-edgehandles + cytoscape-dagre (canvas)
- CodeMirror 6 + @codemirror/lang-yaml (YAML editor)
- Lucide Vue (icons), vue-i18n, @vueuse/core, dayjs

### Infrastructure
- Docker + Docker Compose (5 services: proxy, backend, web, db, redis)
- nginx reverse proxy (SSL, rate limiting, security headers)
- PostgreSQL 16, Redis 7-alpine

---

## RBAC (Full spec in FUNCTIONAL.md §4)

### Roles
| Role | Level | Key permissions |
|------|-------|----------------|
| PlatformSuperAdmin | Platform | All platform config |
| PlatformBillingAdmin | Platform | Invoices only |
| PlatformSupport | Platform | View org status only |
| OrgOwner | Organisation | All workspaces, billing, SSO |
| WorkspaceAdmin | Workspace | Full workspace control |
| Designer | Workspace | Create/edit/publish dev+staging |
| Operator | Workspace | Trigger/cancel/retry/view all |
| Runner | Workspace | Trigger assigned, view own |
| Viewer | Workspace | Read-only, no variables |

Role hierarchy (numeric): Admin=5, Designer=4, Operator=3, Runner=2, Viewer=1

### Seeding
Executor seeds all roles and permissions via EF Core data seeding on first migration.

---

## AI Model Governance (Full spec in FUNCTIONAL.md §5)

### Seven Platform AI Functions
| ID | Name | Default model | Key rule |
|----|------|--------------|---------|
| F1 | Co-pilot | claude-haiku-4-5 | Pattern match first — 80% zero cost |
| F2 | NL→YAML generation | claude-sonnet-4-6 | Quality critical |
| F3 | Visual input parsing | claude-sonnet-4-6 | Vision REQUIRED |
| F4 | Workflow node execution | claude-haiku-4-5 | User controls at node level |
| F5 | Process Intelligence | claude-haiku-4-5 | Batch, low cost |
| F6 | Help AI assistant | claude-sonnet-4-6 | Platform key only |
| F7 | Document/SOP parsing | claude-sonnet-4-6 | Long context needed |

### Resolution Hierarchy
node → workflow → workspace → org → platform (first defined wins)

### Cost Infrastructure (Phase 1 — before any AI feature)
- ai_token_usage table (mandatory metering on every call)
- workspace_ai_budget table (hard cap enforcement)
- semantic_cache (Redis, 24h TTL)
- rate_limit_service (60 Co-pilot calls/user/hour)

---

## Phase Structure

| Phase | Title | Prompts | Status |
|-------|-------|---------|--------|
| 01 | Foundation — Infra, Auth, Org, Workspace, Help | 9 | Pending |
| fix-01 | Phase 1 Fixes | TBD | Pending |
| 02 | Workflow Engine — Durable Execution + Observability | 8 | Pending |
| fix-02 | Phase 2 Fixes | TBD | Pending |
| 03 | SFG Canvas + All Creation Methods + Magic Features | 8 | Pending |
| fix-03 | Phase 3 Fixes | TBD | Pending |
| 04 | Connectors + AI Nodes + Human Gates + DNA | 8 | Pending |
| fix-04 | Phase 4 Fixes | TBD | Pending |
| 05 | Git Versioning + CLI + Developer Tools + Intelligence | 7 | Pending |
| fix-05 | Phase 5 Fixes | TBD | Pending |
| 06 | Marketplace + Public API + SSO + Community Edition | 7 | Pending |
| fix-06 | Phase 6 Fixes | TBD | Pending |

---

## Agent Roster

All agents are in `.claude/agents/`. PM invokes them per the execution model.

| Agent | File | Invoked |
|-------|------|---------|
| PM | pm.md | Always — orchestrates all others |
| Executor | executor.md | Every prompt |
| Verifier | verifier.md | Every prompt (shallow) + phase end (deep) |
| Security | security.md | Phase end + Security-declared prompts |
| UX | ux.md | UX-declared prompts (pre + post execution) |
| UI | ui.md | UI-declared prompts (post execution) |
| Testing | testing.md | Phase end — all active layers |
| **Git** | **git.md** | **Before any release + after each phase (branch hygiene) + Library PR decisions** |

Git Agent mandate: repository governance, branch protection enforcement,
contributor PR management (Library connector/template auto-merge decisions),
Library GitHub sync protocol, release tagging, secrets governance.

---

## Critical Non-Negotiable Rules (Verifier flags violations as Critical)

### 1. Workspace Isolation
Every database query for workspace-scoped data MUST include workspace_id filter.
No exceptions. No raw SQL without workspace_id in WHERE clause.
Cross-workspace data access is impossible at every layer.

### 2. Credential Security
Credential values NEVER: stored plain, returned after creation, logged, in webhook payloads.
Only credential aliases are ever visible.

### 3. AI Call Metering
Every AI call MUST record: tokens_input, tokens_output, model_id, function_id, workspace_id, cost_usd.
No AI feature ships without metering. No exceptions.

### 4. No Hardcoded Model IDs
Every AI call goes through ResolveModelConfig(function_id, workspace_id).
Never hardcode "claude-sonnet-4-6" in business logic.

### 5. YAML Always via YamlDotNet
Never raw string manipulation of workflow YAML.
Always parse/serialise through YamlDotNet with strict deserialiser.

### 6. Git Operations Always via WorkspaceGitService
Never call LibGit2Sharp directly from controllers or application services.

### 7. Zero Style Blocks
Not one <style> block in any Vue component. Ever.

### 8. Every Error Is Actionable
No error message says "Something went wrong." Every error: what happened + why + what to do next.

### 9. Empty States Teach
No empty state says "No results." Every empty state: what to do next + how + call to action.

### 10. Cost Viability Gate
Before any AI feature ships: "What does this cost at 10,000 active users?" If > 5% of projected MRR, more cost levers needed.

---

## Executor Rules — This Project

1. Read FUNCTIONAL.md before starting any prompt — it is the requirements source of truth
2. Clean architecture strictly enforced: Api → Application → Core → Infrastructure. No violations.
3. All entities include: id (Guid), created_at, updated_at. Workspace-scoped entities add workspace_id (never nullable).
4. Soft delete on major entities: is_deleted, deleted_at. Soft delete query filter on all BaseEntity types.
5. YAML parsing always YamlDotNet strict deserialiser — never raw string manipulation
6. Git operations always via WorkspaceGitService
7. AI calls always via ResolveModelConfig() — never hardcoded model IDs
8. Every AI call metered — every call records to ai_token_usage
9. Pattern matching BEFORE AI call in Co-pilot (80% should be zero AI cost)
10. Vue: zero style blocks, Composition API only, TypeScript strict, all API calls via service layer
11. Canvas (Cytoscape.js): must support all 11 interactions from FUNCTIONAL.md §6.6
12. Every help article referenced in error messages must exist (or be queued for content)
13. Every new screen has: loading state, empty state, error state — all three designed
14. Serilog on every function: entry, exit, error — no silent functions
15. Scalar at /scalar on every backend prompt — verify it returns 200

---

## Staging Environment

Staging URL: not yet available — ZAP scan will be skipped for all phases.

---

## Performance-Critical Endpoints

- POST /api/v1/workflows/{id}/instances (trigger — fast path, no heavy ops)
- GET /api/v1/instances/{id} (status polling — cached)
- POST /api/v1/auth/token (login — rate limited)
- POST /api/v1/ai/copilot (Co-pilot — semantic cache first)

---

## Critical User Journeys (E2E — Playwright)

- Register organisation → create workspace → complete onboarding checklist
- Create workflow from plain English → view canvas → trigger instance → watch run
- Upload whiteboard photo → review detections → view generated canvas
- Paste Slack thread → confirm extraction → view generated workflow
- Login → workspace dashboard → workflow health scores visible
- Workflow Interpreter → generate audit narrative → download
- Run limit warning → upgrade prompt → upgrade flow
- Logout → redirect to login

---

## Project Structure

```
flowamaz/
  CLAUDE.md                  — this file (execution constitution)
  FUNCTIONAL.md              — complete requirements bible
  checkpoint.md              — PM agent state
  results.md                 — append-only execution log
  .env.example               — all env vars, no values
  .gitignore
  .claude/
    agents/                  — all 7 Foundation agent files
    hooks/                   — notify-phase-complete.sh, clear-after-prompt.sh
    settings.json
  backend/
    Flowamaz.sln
    Flowamaz.Api/
    Flowamaz.Core/
    Flowamaz.Application/
    Flowamaz.Infrastructure/
    Flowamaz.Tests.Unit/
    Flowamaz.Tests.Integration/
  web/
    src/
      components/
      views/
      composables/
      stores/
      services/
      router/
      types/
      utils/
    e2e/
  infrastructure/
    docker-compose.yml
    nginx/
  docs/
    README.md
    DEVELOPMENT.md
    DEPLOYMENT.md
  prompts/
    phase-01/   (9 prompts)
    phase-02/   (8 prompts — generated after Phase 1 report)
    phase-03/   (8 prompts — generated after Phase 2 report)
    phase-04/   (8 prompts — generated after Phase 3 report)
    phase-05/   (7 prompts — generated after Phase 4 report)
    phase-06/   (7 prompts — generated after Phase 5 report)
```

---

## Token Management

- /clear after every prompt — no exceptions
- /compact only as emergency mid-prompt valve
- PM reads from files only — never from conversation history
- After /clear: read CLAUDE.md → read FUNCTIONAL.md → read checkpoint.md → execute

---

## Fix Loop

Manual fix loop. All agent reports go into phase report. Chat generates fix phase prompts. Human approves before Claude Code executes.

---

## Definition of Done — Per Prompt

- [ ] All declared output files exist
- [ ] dotnet build — zero errors, zero warnings
- [ ] Vue TypeScript — zero errors
- [ ] Unit tests for all new service methods, all pass
- [ ] Service layer coverage ≥ 80%
- [ ] Serilog entry/exit/error on all new backend functions
- [ ] Zero Vue style blocks
- [ ] Scalar at /scalar returns 200 (backend prompts)
- [ ] Workspace isolation enforced on all scoped queries
- [ ] Credential values never in any log statement
- [ ] AI calls metered (if prompt includes AI calls)
- [ ] Loading, empty, error states designed for all new screens
- [ ] Every error message is actionable (not "Something went wrong")
- [ ] Shallow verification passed
- [ ] Result logged to results.md
- [ ] /clear executed

## Definition of Done — Per Phase

- [ ] All prompts completed or skipped with documented reason
- [ ] Verifier deep review complete
- [ ] UX review complete (applicable prompts)
- [ ] UI review complete (applicable prompts)
- [ ] Testing Agent complete — backend + web
- [ ] Security Agent complete — static + Trivy (ZAP skipped — no staging URL)
- [ ] Phase report compiled and at project root
- [ ] Desktop notification fired
- [ ] Phase report uploaded to Chat
