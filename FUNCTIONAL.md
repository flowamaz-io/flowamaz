# Flowamaz — Complete Functional Requirements
# The Single Source of Truth for Everything We Are Building

Version: 1.0  
Date: 2026-05-24  
Status: Approved — all decisions locked  
This document covers all 6 phases + 5-year roadmap.  
Every agent reads this. Every prompt references it. Nothing is built that contradicts it.

---

## 1. PRODUCT IDENTITY

### 1.1 Name and Branding
- Product name: **Flowamaz**
- Primary domain: flowamaz.com (marketing site, main entry point)
- Technical domain: flowamaz.io (developer infrastructure — all subdomains live here)
- Redirect domain: flowamaze.com → flowamaz.com (permanent 301)
- CLI prefix: `fmz`
- npm namespace: `@flowamaz`
- GitHub org: `github.com/flowamaz-io` (standalone org — not under Tootker Solutions)
- Tagline: "The only workflow platform where you describe what you want in plain English, watch it build itself, and trust it to run reliably — forever."

### 1.2 Domain Architecture
```
flowamaz.com        → marketing site, pricing, blog (primary brand domain)
flowamaze.com       → 301 redirect to flowamaz.com
app.flowamaz.io     → the application (Cloud SaaS)
api.flowamaz.io     → REST API endpoint
docs.flowamaz.io    → documentation site
status.flowamaz.io  → status page
get.flowamaz.io     → community edition installer
hooks.flowamaz.io   → webhook receiver endpoint
```

### 1.3 Email Infrastructure
- Team email: team@flowamaz.io
- Email provider: Microsoft 365 Business Basic ($6/user/month)
- Domain: flowamaz.io
- Transactional email: Resend (noreply@flowamaz.io)

| Address | Type | Purpose |
|---------|------|---------|
| team@flowamaz.io | Distribution group | General team contact |
| hello@flowamaz.io | Distribution group | Website contact form |
| support@flowamaz.io | Shared mailbox | Customer support |
| security@flowamaz.io | Shared mailbox | Vulnerability disclosures |
| billing@flowamaz.io | Distribution group | Invoice queries |
| contributors@flowamaz.io | Distribution group | Open source contributor queries |
| noreply@flowamaz.io | Resend (transactional) | App notifications, password resets |

### 1.4 GitHub Repository Map
| Repository | Visibility | Licence | Purpose |
|-----------|-----------|---------|---------|
| flowamaz-io/flowamaz | Public | MIT + BSL | Engine, CLI, canvas, SDK, connectors, community |
| flowamaz-io/platform | Private | Proprietary | Cloud platform — RBAC, billing, SSO, marketplace |
| flowamaz-io/connectors | Public | MIT | Community connector registry (Library backend) |
| flowamaz-io/templates | Public | MIT | Community workflow template library (Library backend) |
| flowamaz-io/docs | Public | CC BY 4.0 | docs.flowamaz.io content |
| flowamaz-io/infra | Private | Proprietary | Terraform/infrastructure |

### 1.5 Product Description
Flowamaz is a next-generation AI-native workflow orchestration platform. It enables business teams and engineering teams to design, execute, monitor, and improve durable workflows — from purchase approvals and employee onboarding to AI agent pipelines and compliance automation.

Users can create workflows from plain English text, voice input, whiteboard photos, paper sketches, Slack conversations, SOP documents, or by drawing directly on the canvas. The platform provides durable execution with exactly-once guarantees, Git-native versioning, a connector marketplace, and a model-flexible AI layer that supports every major LLM provider.

### 1.6 Three Editions
| Edition | Deployment | Price | Target |
|---------|-----------|-------|--------|
| Community | Self-hosted | Free | Developers, evaluation |
| Cloud | SaaS managed | $49/$199/custom | Teams, enterprises |
| Enterprise Self-Hosted | Customer infra | Annual licence | Regulated enterprises |

### 1.7 Market Position
- Not BPMN-first (Camunda/Flowable): no XML, no process analyst required
- Not simple integration (n8n/Zapier): durable execution, saga, compliance, AI orchestration
- Not code-only (Temporal): full visual canvas + business user access
- **Owns the gap**: AI-native + durable execution + human accessible + developer loved

---

## 2. ARCHITECTURE DECISIONS (ALL LOCKED)

### 2.1 Four-Tier Hierarchy
```
Platform (Flowamaz team)
  └── Organisation (company/customer)
        └── Workspace (team/department)
              └── Environment (dev / staging / production)
```

### 2.2 Core Concepts
- **Workflow Definition**: YAML + SFG canvas + .flowamaz-def.md (NL description)
- **Workflow Instance**: one execution of a definition, pinned to commit SHA forever
- **Node**: a single step in a workflow (6 types)
- **Connector**: an integration with an external system
- **Credential**: encrypted API key/token stored in workspace vault
- **Gate**: a human approval checkpoint that pauses execution

### 2.3 Six SFG Node Types
| Node | Colour | Purpose |
|------|--------|---------|
| Trigger | Purple #534AB7 | What starts the workflow |
| Action | Teal #1D9E75 | What the workflow does (API calls, DB ops) |
| AI | Blue #378ADD | LLM inference step |
| Human Gate | Amber #EF9F27 | Human approval / decision checkpoint |
| Router | Coral #E24B4A | Conditional branching |
| End | Gray #888780 | Terminal state |

### 2.4 Eight Programming Blocks
forEach, parallel, try/catch, wait/delay, if/else, case/switch, while, sub-workflow

### 2.5 Durable Execution Model
- Event sourcing: append-only event log is source of truth
- Exactly-once: event log check + connector idempotency key + DB unique constraint
- Worker lease: 30s, renewed every 10s
- Instance state: persisted to snapshot table for fast reads
- Long-running: instances survive server restarts, network failures, deployments

### 2.6 Git-Native Versioning
- Branch = draft
- PR = review request
- Tag = published version
- Commit SHA pinned to every instance at trigger time (immutable forever)
- Two modes: Portal Git (internal store) and Git-backed (mirror to GitHub/GitLab)

### 2.7 Saga Compensation
- Orchestration-based (not choreography)
- Three strategies: Backward (undo all), Forward (retry failed), Pivot (backward before, forward after)
- Each action node declares its compensate: block
- Non-compensatable steps use forward recovery
- Compensation failure → compensation-failed state + manual resolution API

### 2.8 Scalability Tiers
- Tier 1 (Phase 1-2): Postgres SKIP LOCKED, up to 10k concurrent instances
- Tier 2 (Year 2): Redis Streams, up to 100k concurrent
- Tier 3 (Year 3+): Kafka + Citus, up to 500k+

---

## 3. TECH STACK

### 3.1 Backend
- Framework: .NET (latest stable, no preview)
- Architecture: Clean — Api / Core / Application / Infrastructure
- ORM: Entity Framework Core, code-first, lazy loading
- Database: PostgreSQL (latest stable)
- Cache/Queue: Redis (StackExchange.Redis)
- API docs: Scalar.AspNetCore at /scalar (always public)
- Logging: Serilog, structured JSON, entry/exit/error on every function
- Validation: FluentValidation on all request DTOs
- Response: AutoWrapper on all endpoints
- Auth: Custom JWT (not ASP.NET Identity)
- Background jobs: Quartz.NET (timers), Hangfire (webhook delivery, metering)
- YAML: YamlDotNet
- Git ops: LibGit2Sharp
- AI: Anthropic.SDK + provider-agnostic AI service
- Schema validation: NJsonSchema

### 3.2 Frontend
- Framework: Vue 3, Composition API only, TypeScript strict
- Styling: Tailwind CSS v4 — zero style blocks ever
- State: Pinia
- Router: Vue Router 4
- HTTP: Axios with interceptors
- Forms: VeeValidate + Zod
- Canvas: Cytoscape.js + cytoscape-edgehandles + cytoscape-dagre
- YAML editor: CodeMirror 6
- i18n: vue-i18n (English Phase 1, multi-language Year 2)
- Charts: Recharts
- Icons: Lucide Vue

### 3.3 Infrastructure
- Containerisation: Docker + Docker Compose
- Reverse proxy: nginx
- Cloud: AWS Lightsail (primary), Ubuntu LTS
- SSL: Let's Encrypt (production), self-signed (local dev)

### 3.4 Mobile (Year 2)
- Flutter (latest stable), Dart strict null safety
- State: Riverpod
- HTTP: Dio
- Navigation: GoRouter
- Storage: flutter_secure_storage

---

## 4. RBAC — COMPLETE PERMISSION MODEL

### 4.1 Platform Roles (Flowamaz internal)
| Role | Scope | Can do |
|------|-------|--------|
| PlatformSuperAdmin | Global | Everything: plans, billing, AI catalogue, platform config |
| PlatformBillingAdmin | Global | Invoice and payment management only |
| PlatformSupport | Global | View org subscription status — no org data |

### 4.2 Organisation Roles
| Role | Can do |
|------|--------|
| OrgOwner | All workspaces, billing portal, SSO config, org-wide AI model policy |

### 4.3 Workspace Roles (all scoped to workspace)
| Role | Can do |
|------|--------|
| WorkspaceAdmin | Full control: publish to production, credentials, members, settings |
| Designer | Create/edit workflows, publish dev/staging, use connectors |
| Operator | Trigger, cancel, retry runs, view all run details and variables |
| Runner | Trigger assigned workflows, view own runs only |
| Viewer | Read-only: definitions and run history, no variable values |

### 4.4 Role Hierarchy (numeric for comparison)
Admin=5, Designer=4, Operator=3, Runner=2, Viewer=1

### 4.5 Complete Permission Map
```
WorkspaceAdmin:     workflows.*, instances.*, credentials.manage,
                    connectors.configure, members.manage,
                    workspace.settings, audit.read, gates.decide

Designer:           workflows.read, workflows.create, workflows.update,
                    workflows.publish.dev, workflows.publish.staging,
                    instances.read, instances.trigger, instances.cancel,
                    instances.retry, instances.variables.read, gates.decide.assigned

Operator:           workflows.read, instances.read, instances.trigger,
                    instances.cancel, instances.retry,
                    instances.variables.read, audit.read, gates.decide.assigned

Runner:             instances.trigger.assigned, instances.read.own

Viewer:             workflows.read, instances.read (no variables)
```

### 4.6 API Key Scopes
workflows:read, workflows:trigger, instances:read, instances:write,
webhooks:manage, instances:variables:read

### 4.7 API Key Format
```
Production:  fmz_live_{workspace_slug}_{env}_{random_32_chars}
Dev/Staging: fmz_test_{workspace_slug}_{env}_{random_32_chars}
```

### 4.8 HTTP Status Codes (confirmed fix-01)
Business-rule exceptions map to these HTTP status codes. The mapping lives on each
`AppException` subclass (`HttpStatusCode`) and is emitted verbatim by `GlobalExceptionMiddleware`.
- `ConfigViolationException` → **422 Unprocessable Entity** (well-formed request, semantically
  invalid input — e.g. provider not in the org allowlist, model fails a capability gate). 422 is
  correct here; 400 is reserved for malformed requests.
- `SelfModificationException` → **409 Conflict** (changing/removing your own role conflicts with
  the current resource state).
- `LastAdminException` → **409 Conflict** (removing/demoting the last Admin conflicts with the
  workspace's invariant of keeping ≥1 Admin).
- `InsufficientRoleException` → **403 Forbidden**.
- `InvalidCredentialsException` and `AccountLockedException` → **401 Unauthorized** (no user
  enumeration; the lockout message differs but the status is the same).
- `AlreadyMemberException` and `SlugAlreadyExistsException` → **409 Conflict**;
  `UserNotInOrganisationException` → **404 Not Found**; `RateLimitExceededException` → **429**.
- Workspace-not-found / wrong-org → **404 Not Found** (never 403, to avoid resource enumeration).
- FluentValidation failures → **400 Bad Request** (`VALIDATION_ERROR`).

---

## 5. AI MODEL GOVERNANCE

### 5.1 Provider Groups
| Provider | Models | Notes |
|----------|--------|-------|
| Anthropic | claude-haiku-4-5, claude-sonnet-4-6, claude-opus-4-6 | Platform default provider |
| Microsoft Azure OpenAI | gpt-4o, gpt-4o-mini, o1-mini | BYOK only |
| Google Vertex AI | gemini-2.0-flash, gemini-1.5-pro, gemini-1.5-flash | BYOK only |
| Kimi (Moonshot AI) | moonshot-v1-128k, moonshot-v1-32k, moonshot-v1-8k | BYOK only, strong Chinese language |
| Mistral AI | mistral-large, mistral-medium, mistral-nemo | BYOK only |
| BYOM | Any OpenAI-compatible endpoint | Enterprise plan only |

### 5.2 Seven Platform AI Functions
| ID | Function | Platform Default | Min Capability | BYOK |
|----|----------|-----------------|----------------|------|
| F1 | Co-pilot (in-canvas assistant) | claude-haiku-4-5 | Fast, JSON mode | Yes |
| F2 | NL→YAML generation | claude-sonnet-4-6 | 200k ctx, reasoning | Yes |
| F3 | Visual input parsing (image→workflow) | claude-sonnet-4-6 | Vision mandatory | Yes |
| F4 | Workflow node execution (runtime) | claude-haiku-4-5 | Any | Yes + BYOM |
| F5 | Process Intelligence (batch analytics) | claude-haiku-4-5 | JSON mode | Yes |
| F6 | Help AI assistant (in-app Q&A) | claude-sonnet-4-6 | 200k ctx | Platform only |
| F7 | Document/SOP parsing | claude-sonnet-4-6 | Long context | Yes |

### 5.3 Model Selection Hierarchy (all 5 levels)
```
1. Platform admin config (per function)        — fallback for all
2. Organisation admin config (per function)    — overrides platform
3. Workspace admin config (per function)       — overrides org
4. Workflow YAML header (default_model)        — overrides workspace (F4 only)
5. AI node model: field                        — overrides workflow (F4 only)
```

Resolution: walk from node → workflow → workspace → org → platform. First defined wins.

### 5.4 Capability Enforcement
- Capability flags per model: vision, max_context_tokens, json_mode, streaming
- F3 (visual input) REQUIRES vision capability — enforced at config-save time
- F7 (document parsing) REQUIRES min 100k context — enforced at config-save time
- Plan tier access: Community=Anthropic only, Starter=+Azure+Google, Pro=all, Enterprise=all+BYOM

### 5.5 AI Cost Control Architecture (12 levers — ALL active from Phase 1)
1. Model tiering per function (Haiku for frequent, Sonnet for quality-critical)
2. Prompt caching on system prompts (cached = 10% cost)
3. Semantic deduplication cache (Redis, 24h TTL — same command = cache hit)
4. Context window minimisation (send relevant subgraph only, not full YAML)
5. Template-first pattern matching (80% of Co-pilot = zero AI call)
6. JSON diff output format (patches not full YAML — 80-90% output token reduction)
7. Debounce Co-pilot calls (800ms after user stops typing)
8. Per-workspace hard budget cap (enforced, not advisory)
9. BYOK for Pro/Enterprise Co-pilot (cost shifts to customer)
10. Streaming responses (zero additional cost, 70% perceived latency reduction)
11. Real-time platform admin AI cost dashboard (anomaly detection)
12. Cost viability gate before shipping any AI feature

**Cost infrastructure required in Phase 1** (before any AI feature):
- ai_token_usage table (every call: tokens_input, tokens_output, model, function, workspace, cost_usd)
- workspace_ai_budget table (limit, used, reset_date)
- platform_ai_cost_view (real-time dashboard data)
- Rate limiting service (user-level: 60 Co-pilot calls/hour)
- Semantic cache service (Redis)
- Prompt cache key generator

---

## 6. WORKFLOW CREATION METHODS (ALL SIX)

### 6.1 Method 1 — Natural Language Template (Plain English)
Six-section structured template:
1. What is this workflow (name, purpose, who uses it)
2. What starts it (trigger type, input fields and types)
3. The steps (each step: what happens, who/what system, decisions, branches)
4. Rules and constraints (thresholds, SLA, failure handling)
5. What it produces (output variables, external notifications)
6. Systems and AI (connectors needed, AI model preference)

AI generates complete YAML in one pass. Max 3-5 clarifying questions asked all at once. Validation runs automatically. YAML + canvas appear together. .flowamaz-def.md committed alongside YAML.

### 6.2 Method 2 — Voice Input
User speaks instead of typing. Voice → text → same template pipeline → YAML. Streaming transcription (Web Speech API or Whisper). Same output as Method 1.

### 6.3 Method 3 — Visual Input (Whiteboard / Paper / Diagram)
Upload: photo of whiteboard, paper sketch, existing Visio/Lucidchart/PowerPoint diagram.
Pipeline:
1. Client-side pre-processing (resize, contrast, format convert — zero cost)
2. Claude Sonnet vision: image → structured JSON (nodes, edges, labels, confidence scores)
3. Connector matching from label text (zero AI cost)
4. YAML generation from JSON (Claude Haiku — deterministic fill)
5. Low-confidence elements (< 80%) shown for user confirmation (typically 2-3 questions)
6. Canvas populated, original image stored as workflow reference

Cost: ~$0.01-0.02 per upload.

### 6.4 Method 4 — Conversation Import (Slack / Teams / Email)
Paste a Slack thread, Teams meeting transcript, or email chain where a process was discussed.
AI extracts: who said what, what they agreed to, the implied process structure, system mentions, approver names, and conditions. Generates workflow directly.
Input types: plain text paste, Slack export JSON, email thread.

### 6.5 Method 5 — SOP / Document Upload
Upload PDF, DOCX, or text file containing a Standard Operating Procedure or business process document.
AI reads the document, extracts process steps, decision points, roles, systems, and conditions.
Generates workflow with confidence score per section. User confirms low-confidence extractions.
Supports multilingual documents (Year 2).

### 6.6 Method 6 — Canvas (Drag and Drop)
Full interactive visual canvas built on Cytoscape.js:
- Drag nodes from left palette to canvas
- Drag from node output handles to create edges
- Move nodes by drag
- Multi-select (rubber-band drag) and group move
- Undo/redo (Ctrl+Z / Ctrl+Y) — history stack of YAML snapshots
- Auto-layout (dagre algorithm — "Tidy" button)
- Edge condition labels on router outputs
- Group containers / swimlanes (visual grouping of related nodes)
- Sticky notes / annotations (type: annotation, ignored by engine)
- Snap to grid
- Minimap (functional, clickable)
- YAML ↔ canvas bidirectional sync (click node → jump to YAML line; edit YAML → canvas updates live)
- Right-click context menu on nodes (delete, help, duplicate, add connected node)

---

## 7. WORKFLOW CO-PILOT

In-canvas AI assistant. Always present. Accepts natural language commands.

### 7.1 Command Examples
- "Add a 48h timeout to the CFO gate with escalation to COO"
- "Connect validate-employee to risk-check"
- "Add a retry 3 times on the SAP connector step"
- "What happens if the Slack connector is unavailable here?"
- "Add an error handler that sends email to ops team on failure"
- "Make the router condition check if department equals Finance"

### 7.2 Co-pilot Pipeline (cost-optimised)
```
Request received
  → Rate check (user at limit? workspace at budget cap?)
  → Pattern match (known command? apply template — NO AI CALL)
  → Semantic cache check (same command recently? return cached diff — NO AI CALL)
  → Context extraction (relevant subgraph only — not full YAML)
  → Model selection (simple → Haiku, complex → Sonnet)
  → Cached system prompt + fresh dynamic part
  → Response: JSON patch only (not full YAML)
  → Stream to canvas token-by-token
  → Cache write (24h TTL)
  → Metering record
```

### 7.3 Pattern Library (zero AI cost)
20 common patterns handled by template application:
add-timeout, add-retry, add-notification, connect-nodes, add-error-handler,
add-router-condition, add-human-gate, set-model, add-parallel-block,
add-foreach-block, add-try-catch, add-wait, set-sla, add-compensation,
add-annotation, duplicate-node, delete-node, rename-node, add-variable, set-credential

---

## 8. MAGIC FEATURES (UNIQUE — NOBODY HAS THESE)

### 8.1 Workflow Twin — Shadow Simulation
Every live workflow automatically runs silent shadow simulations with thousands of input variations. Finds the inputs that break the workflow before real users do. Results shown as health badges on workflow cards.
"Your purchase approval breaks when amount equals exactly $5,000 — router uses > not ≥."
Phase: Year 2 (requires execution engine + simulation layer).

### 8.2 Workflow Empathy View
Two views per workflow: Technical SFG canvas + Empathy View.
Empathy View shows: emails sent to requester, wait periods, visibility gaps, days to outcome, human confusion points. Flags friction with specific improvement suggestions.
"Your onboarding workflow sends 7 emails in 3 days and gives the new hire zero status updates."
Phase: Phase 4 (requires instance detail data from Phase 2).

### 8.3 Workflow Memory
Each workflow accumulates learning from every run. Surfaces intelligent insights during execution: fastest approver patterns, inputs that typically get rejected, vendor risk history, SLA probability.
Phase: Year 2 (requires 6+ months of execution data).

### 8.4 Conversation→Workflow
Paste a Slack thread / Teams transcript / email chain → AI extracts the process → generates workflow. Same pipeline as Method 4 above.
Phase: Phase 3 (same NL→YAML pipeline, different input format).

### 8.5 Workflow Negotiator
When two systems return conflicting data, AI mediates. Detects conflict, attempts autonomous resolution using business rules, escalates to human with clear recommendation if needed.
Phase: Year 2.

### 8.6 Workflow Archaeology
Connect to existing systems (email, ERP, Slack, HR). AI observes historical data patterns → reverse-engineers the undocumented manual workflow → shows it as a Flowamaz canvas.
"Based on 180 days of email and ERP data, here is the process your team has been running manually."
Phase: Year 3 (requires broad connector depth + process mining layer).

### 8.7 Workflow Weather — Executive Health Map
Real-time organisational health map. Green/Yellow/Orange/Red zones per team/department. Like a weather map for operational health. COO opens at 7am, sees the orange zone, drills in, acts.
Phase: Phase 3 (built on top of instance metrics from Phase 2).

### 8.8 Workflow DNA — Similarity and Inheritance
Every workflow has a DNA fingerprint (trigger type, decision topology, connector mix, SLA profile, AI usage). New workflow creation shows: "87% similar to Purchase Approval — clone it?" Underperforming workflows inherit patterns from high-performing siblings.
Phase: Phase 4.

### 8.9 Workflow Economy
Complete workflow marketplace. Publish workflows (not just templates/connectors) and earn revenue. Domain experts sell industry-specific workflows. 20% Flowamaz / 80% publisher. Ongoing subscription revenue per workflow.
Phase: Phase 6.

### 8.10 Workflow Interpreter — Stakeholder Narratives
Generate audience-specific explanations from any running instance on demand.
CEO: "Your request is waiting for CFO approval. Expected by tomorrow 3pm."
Auditor: "On 14 March 2026, request RM47,500 was submitted by Anita Kumar, approved by Rajan Mehta at 14:23 MYT, PO FM-20026 created in SAP at 14:31 MYT." (one click → audit-ready document)
Developer: full technical execution log.
Phase: Phase 2 (execution data exists, just needs LLM narrative layer).

### 8.11 Workflow Consciousness — Epistemic Honesty
Platform surfaces what it does not know. Low-confidence AI outputs flagged. Connector behaviour changes detected before failure. Regulatory change alerts ("This workflow predates PDPA 2024 — review recommended"). Six-month stale workflow alerts.
Phase: Year 2.

---

## 9. CONNECTOR ECOSYSTEM

### 9.1 Official Connectors (MIT, Phase 4) — 13 connectors
| Connector | Category | Auth | Notes |
|-----------|----------|------|-------|
| HTTP/REST | Core | API key / Bearer | Generic HTTP — any REST API |
| PostgreSQL | Database | Connection string | Query, insert, update, delete |
| MySQL | Database | Connection string | Query, insert, update, delete |
| Slack | Messaging | OAuth 2.0 | Send message, interactive approval buttons |
| Microsoft Teams | Messaging | OAuth 2.0 | Send message, adaptive card approvals — enterprise default |
| Email (SMTP/Resend) | Messaging | API key | Transactional email — noreply@flowamaz.io pattern |
| Webhook emit | Core | HMAC signed | Outbound webhook with signature |
| Webhook receive | Core | HMAC validated | Inbound trigger endpoint |
| File system | Core | None | Local file read/write |
| Schedule/Cron | Core | None | Time-based triggers |
| Script/Shell | Core | None | Execute shell scripts |
| GitHub | DevOps | OAuth / PAT | Issues, PRs, deployments |
| Microsoft 365 | Productivity | OAuth 2.0 | Email, calendar, SharePoint via Microsoft Graph API |

Note: Teams connector built as first-class alongside Slack — many enterprise customers use Teams, not Slack. Human gate approval via Teams Adaptive Card has identical feature parity with Slack.

### 9.2 Marketplace Connector Tiers
| Tier | Review | Source | Available on |
|------|--------|--------|-------------|
| Official | FlowMind team | Open source MIT | All plans |
| Verified | 3-5 day security audit | Source inspectable | Cloud plans |
| Community | Automated scan only | Open source | Community + Cloud |

### 9.3 Connector Specification
Every connector declares:
- connector.yaml manifest (id, publisher, version, auth type, network.allowed_domains, operations[])
- Per-operation spec (input_schema, output_schema, errors, idempotency, rate_limit)
- Auth patterns: api-key, oauth2-auth-code, oauth2-client-credentials, basic, aws-sigv4, custom

### 9.4 Guided OAuth Credential Setup
Select connector → "Connect with OAuth" → browser OAuth flow opens → token stored encrypted → done. No Client ID, no redirect URL, no manual config. Addresses n8n's #1 UX complaint.

### 9.5 Connector Sandbox
- Process isolation (separate container)
- Input scoping (only declared input variables passed — not full workflow context)
- Network policy (egress proxy enforces domain allowlist — infrastructure level, not code)

### 9.6 Revenue Model
- Free connectors: publisher earns reputation + adoption
- Paid connectors: 20% Flowamaz / 80% publisher, Stripe Connect payouts monthly

---

## 10. HELP SYSTEM (7 LAYERS — ALL FROM PHASE 1)

### 10.1 Layer 1 — In-App Help Panel
Slides from right. Shift+? shortcut. ? button in top bar. Contextual by default (opens to article for current screen). Full navigation tree. Search. Works offline (cached).

### 10.2 Layer 2 — Tooltip Field Help
Every form field, config option, and setting has inline ? icon. Hover → tooltip with field description, valid values, example. Links to full article.

### 10.3 Layer 3 — Getting-Started Checklist
Dashboard persistent checklist for new users. Each item links to screen + help article. Tracked per user. Disappears on completion or dismissal.

### 10.4 Layer 4 — Canvas Node Help (Phase 3)
Right-click any node → "Help with this node type". ? icon in node inspector header. Opens help panel to node-specific article with YAML examples.

### 10.5 Layer 5 — YAML Editor Inline Help (Phase 3)
Hover any field name → tooltip (description, type, valid values, example). Validation error messages link to fix articles. "SCH-003: Unknown field → Did you mean X? [Read more]"

### 10.6 Layer 6 — Error Page Help (Phase 1)
Every error state: what happened + why + what to do next + help article link. No error ever says "Something went wrong." Every error is actionable.

### 10.7 Layer 7 — docs.flowamaz.io (Phase 1)
Public documentation site (Docusaurus/Mintlify). Single source of truth. In-app panel fetches from same source. "Edit on GitHub" on every article. Community can contribute.

### 10.8 Ask AI in Help (Phase 4)
Search fallback: "Didn't find what you need? Ask AI →". Claude with full documentation context. Answer shown inline with source article links. Answers specific "how do I build X" questions with YAML examples.

### 10.9 Content Map — Phase 1 Articles (must ship with Phase 1)
**Getting Started (6):** What is Flowamaz, Community edition install, Cloud signup, Plans comparison, Migrate Community→Cloud, Key concepts glossary
**Organisations & Workspaces (5):** What is a workspace, Invite members, Roles and permissions, API keys, Environments
**Plans & Billing (4):** Plans comparison table, Upgrade guide, Invoice explainer, Enterprise self-hosted
**Install Community (3):** Docker Compose, Single binary (brew/curl/winget), Server VPS deployment
**Troubleshooting (Phase 2):** Why did my instance fail, Connector not connecting, Gate timed out, Run limit reached

### 10.10 Screen → Article Context Map
Every screen maps to a default help article (full map in CLAUDE.md).

---

## 11. OBSERVABILITY AND ANALYTICS

### 11.1 Operations Dashboard
Live run count, queue depth, worker status, active instances by workflow, recent failures, SLA breach alerts.

### 11.2 Process Intelligence
Predictive: "Your CFO approval will breach SLA in 3 days at current rate." Bottleneck analysis per node. AI cost per workflow per month. Runs as hourly batch job (Claude Haiku, structured data input, low cost).

### 11.3 Run Timeline View
Waterfall view per instance. Each node: start time, duration, status, output summary. Like browser DevTools network panel — but for workflow steps. Shows exactly what happened when.

### 11.4 ROI Analytics Dashboard
Per workflow: hours saved per run × runs per month = time saved. Time saved × avg hourly cost = money saved. Cumulative since deployment. "Your purchase approval workflow has saved 1,240 hours and $186,000 since May." No competitor calculates actual ROI.

### 11.5 Workflow Health Score
Every workflow: 0-100 score from test coverage %, SLA compliance rate, error rate (30 days), last reviewed date, documentation completeness, security scan status. Shown on workflow list card.

### 11.6 Workflow Weather (Executive View)
Real-time map of all workspace/team workflows. Green/Yellow/Orange/Red zones. Click zone → drill to workflow → drill to instance. COO morning view.

### 11.7 Audit Log
Immutable. HMAC-SHA256 signing chain per org. Every action by every user. Retention: 1 year minimum, configurable up to 7 years. SIEM streaming. S3/GCS export. Cannot be shortened by workspace admin.

### 11.8 OpenTelemetry Export
OTLP export for custom observability stacks. Available on Pro/Enterprise.

---

## 12. SECURITY

### 12.1 Authentication
- JWT: 15-min access token (HS256), 7-day refresh token (rotation on every use)
- BCrypt password hashing, minimum cost factor 12
- Account lockout: 5 failed attempts → 15 min, stored in Redis
- Rate limiting: 10/min login, 5/hr register (nginx + middleware)
- No user enumeration: same error message for wrong email and wrong password
- Refresh token rotation: old token revoked when new one issued

### 12.2 Encryption
- In transit: TLS 1.3 (TLS 1.2 minimum), HSTS enforced
- At rest: AES-256 (database volumes, object storage, backups)
- Credential vault: AES-256 per-workspace encryption key, keys in KMS
- Sensitive workflow variables (sensitive: true): field-level AES-256 before storage
- Audit log: HMAC-SHA256 signing chain

### 12.3 Workspace Isolation (CRITICAL)
Every database query for workspace-scoped data MUST include workspace_id filter.
Verifier flags missing workspace_id as Critical.
No cross-workspace data access possible at any layer.

### 12.4 Credential Security
Credential values are NEVER:
- Stored in plain text
- Returned after initial creation
- Logged in any Serilog statement
- Included in webhook payloads unless explicitly declared
- Visible to any role (only aliases are shown)

### 12.5 API Key Security
- Stored as SHA256 hash only — never plain
- Format enforced: fmz_live_ or fmz_test_ prefix
- Scope enforcement on every API call
- Rate limited

### 12.6 SSO (Phase 6)
SAML 2.0 + OIDC. Supported IdPs: Okta, Azure AD/Entra, Google Workspace, ADFS, PingFederate, OneLogin, Generic. SCIM 2.0 for automated provisioning. Group→role mapping. JIT provisioning option. SSO enforcement blocks password login. 14-day advance notice on platform default model changes.

### 12.7 Data Residency
Regions: ap-southeast-1 (default), eu-west-1, us-east-1. Immutable after org creation. All workflow data stays in chosen region. Platform layer (billing aggregates only) is global.

### 12.8 CMEK (Year 2)
Customer Managed Encryption Keys. AWS KMS, GCP KMS, Azure Key Vault, HashiCorp Vault. Crypto-shredding by key revocation. Enterprise plan only.

### 12.9 Compliance Targets
- SOC 2 Type II (launch target)
- ISO 27001 (Year 1)
- GDPR (eu-west-1 deployment + DPA available)
- PDPA Malaysia 2024, Singapore, Thailand (ap-southeast-1 deployment)
- HIPAA BAA (Enterprise, Year 2)
- FedRAMP (roadmap, Year 4)

---

## 13. BILLING AND METERING

### 13.1 Six Billable Events
1. Workflow runs (count per instance triggered)
2. AI calls (count + token counts for cost calculation)
3. Active members (peak count in billing period)
4. Storage (bytes at month-end snapshot)
5. API calls (for rate limiting on lower plans)
6. Connector executions (informational, future pricing)

### 13.2 Plan Limits
| Limit | Community | Starter | Pro | Enterprise |
|-------|-----------|---------|-----|-----------|
| Workflows | 5 | 20 | Unlimited | Unlimited |
| Runs/month | 500 | 5,000 | 50,000 | Custom |
| Workspaces | 1 | 3 | 10 | Unlimited |
| Members/workspace | 1 | 10 | 50 | Unlimited |
| Storage | 1 GB | 10 GB | 50 GB | Custom |
| Run retention | 7 days | 90 days | 365 days | Custom |
| API rate limit | 10/min | 60/min | 300/min | Custom |
| AI models | Anthropic only | +Azure+Google | All | All+BYOM |

### 13.3 Metering Architecture
- Async side-channel — NEVER in execution path
- Execution continues even if metering service is down
- Events replayed from event log on recovery
- Invoice line items: counts only, zero workflow content

### 13.4 Overage Caps
- Org-level monthly cap: block new triggers at 100%, notify at 50%/80%
- Workspace-level AI cost budget: alert at 80%, block at 100%

### 13.5 Community Edition Limits Enforcement
Enforced by engine (not honour system). Limits tracked in local SQLite/Postgres. When reached: clear message + upgrade prompt. Never silent failure.

---

## 14. COMMUNITY EDITION

### 14.1 Deployment
```bash
# One command install
curl -fsSL https://get.flowamaz.io/community | sh

# Homebrew
brew install flowamaz/tap/fmz
fmz start

# Docker Compose
docker compose up -d

# Opens at http://localhost:3000
```

### 14.2 What Is Included
Full workflow engine, YAML authoring, SFG canvas (full editing), fmz CLI, all node types and programming blocks, Git-native versioning, dry-run and debugger, VS Code extension, community connectors from GitHub, webhook trigger, basic observability (7-day history), Claude Haiku BYOK for AI.

### 14.3 What Is Not Included
SSO/SAML/SCIM, RBAC (multi-user), audit log, marketplace (managed), analytics dashboards, SLA management, CMEK, data residency, multiple workspaces, API rate limits above 10/min.

### 14.4 Upgrade Path
```bash
fmz cloud migrate --workspace ws_my_team
```
Git repo travels with the user. Workflows import instantly. Credentials need re-entry. Minutes, not hours.

### 14.5 Upgrade Prompts
Shown contextually, not constantly. At 80% of limit, once per month. At workspace invite attempt, once. At model selection blocked, once. Never on every page load.

---

## 15. fmz CLI — COMPLETE COMMAND SET

```
fmz auth         login, logout, whoami, workspace list/switch
fmz workflow     list, show, pull, push, delete
fmz branch       list, create, switch, delete
fmz commit       -m "message"
fmz log          [--workflow id]
fmz diff         [ref1 ref2] [--check-breaking] [--reporter github]
fmz pr           create, list, approve, merge
fmz tag          create, list, rollback
fmz instance     trigger, list, show, watch, cancel, retry
fmz gate         list, decide
fmz test         [--watch] [--coverage] [--min-coverage 80]
fmz validate     [--watch] [--reporter github/junit]
fmz dry-run      --fixture file.json [--show-variables]
fmz debug        instance-id
fmz dev          [--port 3000]
fmz deploy       --env dev/staging/production [--tag v1.0.0]
fmz credential   list, set, delete
fmz connector    list, install, publish, test, validate, build, init
fmz model        list, register, test
fmz marketplace  install, list, update, uninstall, outdated, pin
fmz config       get, set
fmz completion   bash/zsh/fish
fmz cloud        migrate
```

Exit codes: 0=success, 1=error, 2=validation fail, 3=auth error, 4=permission denied, 5=not found, 6=breaking change, 7=coverage below threshold.

Shell completions: complete live resource names (instance IDs, branch names, tag names, workspace names) from API.

---

## 16. VS CODE EXTENSION

- YAML IntelliSense (schema-aware, context-sensitive autocomplete)
- Inline diagnostics (squiggles, Problems panel, all 6 validation layers)
- Live canvas split pane (YAML left, canvas right, sync in both directions)
- Run/dry-run/test from editor (right-click menu, results in output panel)
- Branch and PR panel (source control integration)
- Snippet library (sfg: prefix triggers picker — all node types, blocks, connectors)
- All command palette entries (all fmz commands)
- Hover: field description + type + valid values + example value

---

## 17. GIT-NATIVE VERSIONING — COMPLETE SPEC

### 17.1 Two Modes
- **Portal Git**: internal LibGit2Sharp store, no external account needed
- **Git-backed**: mirrors to GitHub/GitLab, real PRs, real commit history

### 17.2 .flowamaz.yaml (repo config for GitOps)
```yaml
workspace: ws_finance
workflows_path: workflows/
environments:
  dev:
    deploy_on: push
    branch: main
    connectors: mock
  staging:
    deploy_on: push
    branch: main
    connectors: sandbox
  production:
    deploy_on: tag
    pattern: "v[0-9]+.[0-9]+.[0-9]+"
    require_pr_approval: true
    min_approvals: 1
    require_passing_tests: true
breaking_change_policy: warn  # warn | block | allow
```

### 17.3 Breaking Change Detection
Removed operations, changed schemas, renamed node IDs that instances depend on. Detected at fmz diff --check-breaking. GitHub PR annotation via --reporter github. Configurable policy per repo.

### 17.4 Instance Version Pinning
Every instance stores the commit SHA of the workflow YAML it started on. Immutable forever. In-flight instances never affected by new deployments.

---

## 18. OPEN SOURCE STRATEGY

### 18.1 MIT Licensed (fully open)
- Workflow engine core (durable execution, event log, YAML spec)
- SFG canvas renderer
- YAML validator
- fmz CLI
- @flowamaz/connector-sdk
- All official connectors
- VS Code extension
- Workflow definition template

### 18.2 Proprietary (source not published)
- Multi-tenant RBAC engine
- SSO/SAML/OIDC/SCIM
- Billing and metering
- Marketplace infrastructure
- Audit log compliance system
- Analytics and SLA management
- CMEK infrastructure
- Platform admin portal

### 18.3 BSL 1.1 (Community Edition Wrapper)
Self-hosting for own use: permitted. Offering as managed service to others: restricted. Converts to MIT after 4 years.

---

## 19. PRODUCT DESIGN PRINCIPLES (ENFORCED BY VERIFIER)

1. **30-second wow**: Every new user must experience a wow moment within 30 seconds. Describe one sentence → canvas appears. Non-negotiable.
2. **Progressive complexity**: Business analyst and engineer start on the same screen but get different depths. Same product, appropriate complexity per user.
3. **Every empty state teaches**: Never "No results found." Always: what to do next + how to do it + link to help.
4. **Errors as teachers**: Every error message: what happened + why + exact next step + help article link. Zero "Something went wrong."
5. **Speed as a feature**: Canvas < 500ms. Validation real-time. Help panel instant (pre-loaded). AI responses stream immediately.
6. **Delight in the details**: Canvas animations, health score colour changes, completion states, building animations. 2 days each. Makes "talk of the town."
7. **Design quality bar**: Before any screen ships: (a) first-time user knows what to do without reading? (b) feels like Vercel/Linear/Notion quality? (c) all empty, error, loading states designed — not just happy path?

---

## 20. PHASE PLAN — COMPLETE

### Phase 1: Foundation (8 prompts)
Auth, org/workspace/RBAC, API keys, frontend scaffold, Docker deployment, help system infrastructure, onboarding wizard, getting-started checklist, AI cost metering infrastructure, Phase 1 help content.

### Phase 2: Workflow Engine Core (8 prompts)
Workflow + instance entities, event log, task queue, orchestrator, HTTP worker, AI worker (basic), timer scanner, instance API, Workflow Interpreter (stakeholder narratives), run timeline view, step debugger.

### Phase 3: SFG Canvas + Creation (8 prompts)
Full interactive canvas (all 11 interactions), YAML editor + validator, NL→YAML generation, voice input, conversation→workflow, visual input (whiteboard/paper), workflow list view, Workflow Weather executive dashboard, snippet library.

### Phase 4: Connectors + AI + Human Gates (8 prompts)
Connector SDK + spec, 12 official connectors, guided OAuth wizard, AI node worker (multi-model + BYOM + fallback), human gate node, saga/compensation engine, credential vault, Workflow Empathy view, Workflow DNA, connector health dashboard.

### Phase 5: Git Versioning + CLI + Developer Tools (7 prompts)
LibGit2Sharp Git versioning, branch/PR/tag API, fmz CLI (all commands), VS Code extension, CI integration (GitHub Actions + GitLab), GitOps (.flowamaz.yaml), Process Intelligence (predictive analytics), ROI analytics dashboard, Workflow Health Score.

### Phase 6: Marketplace + Public API + Community (7 prompts)
Connector marketplace (publish, review, tiers, paid connectors), Workflow Economy (workflow marketplace), public REST API, TypeScript SDK, Python SDK, webhook delivery + HMAC, SSO/SAML/OIDC/SCIM, community edition packaging, embeddable canvas web component, Help AI assistant (Ask AI).

### Year 2 Features (post Phase 6)
Workflow Twin, Workflow Memory, Workflow Negotiator, Workflow Consciousness, mobile companion app (Flutter), real-time canvas collaboration, CMEK, multi-language NL input.

### Year 3 Features
Workflow Archaeology (process mining), multi-agent orchestration (AI→AI chains), cross-workspace orchestration, advanced process mining, data residency (all 3 regions), FedRAMP.

---

## 21. WHAT MAKES US DIFFERENT FROM EVERY COMPETITOR

| Feature | Flowamaz | Camunda | Flowable | n8n | Temporal |
|---------|---------|---------|---------|-----|---------|
| NL→canvas in plain English | ✓ | ✗ | ✗ | ✗ | ✗ |
| Whiteboard/paper→workflow | ✓ | ✗ | ✗ | ✗ | ✗ |
| Conversation→workflow | ✓ | ✗ | ✗ | ✗ | ✗ |
| AI node (multi-model + BYOM) | ✓ | ✗ | ✗ | Basic | ✗ |
| YAML↔canvas bidirectional sync | ✓ | ✗ | ✗ | ✗ | ✗ |
| Git-native versioning | ✓ | ✗ | ✗ | ✗ | ✗ |
| Durable execution + saga | ✓ | ✓ | ✓ | ✗ | ✓ |
| Guided OAuth wizard | ✓ | ✗ | ✗ | ✗ | ✗ |
| Step debugger with variable inspector | ✓ | Partial | Partial | Basic | ✗ |
| Workflow Weather (executive map) | ✓ | ✗ | ✗ | ✗ | ✗ |
| Workflow Interpreter (stakeholder narratives) | ✓ | ✗ | ✗ | ✗ | ✗ |
| ROI analytics dashboard | ✓ | ✗ | ✗ | ✗ | ✗ |
| In-app contextual help | ✓ | ✗ | ✗ | ✗ | ✗ |
| Workflow health score | ✓ | ✗ | ✗ | ✗ | ✗ |
| Free self-hosted community | ✓ | Declining | ✗ | ✓ | ✗ |
| Process mining / archaeology | Year 3 | ✗ | ✗ | ✗ | ✗ |

---

## 22. ENVIRONMENT VARIABLES (COMPLETE LIST)

```
# Database
DB_CONNECTION_STRING=Host=localhost;Port=5432;Database=flowamaz;Username=flowamaz;Password=
DB_NAME=flowamaz
DB_USER=flowamaz
DB_PASSWORD=

# Redis
REDIS_CONNECTION_STRING=localhost:6379

# JWT
JWT_SECRET=
JWT_ISSUER=flowamaz-api
JWT_AUDIENCE=flowamaz-client
JWT_ACCESS_TOKEN_EXPIRY_MINUTES=15
JWT_REFRESH_TOKEN_EXPIRY_DAYS=7

# AI — Platform managed keys (for F2, F5, F6, F7 on managed plans)
ANTHROPIC_PLATFORM_KEY=
GOOGLE_PLATFORM_KEY=

# AI Cost Control
AI_BUDGET_DEFAULT_MONTHLY_TOKENS=500000
AI_COPILOT_RATE_LIMIT_PER_HOUR=60
AI_SEMANTIC_CACHE_TTL_HOURS=24

# Git Storage
GIT_REPOS_BASE_PATH=/app/data/repos

# Platform
PROJECT_NAME=flowamaz
ASPNETCORE_ENVIRONMENT=Development
PLATFORM_BASE_URL=http://localhost:5000

# Frontend
VITE_API_BASE_URL=http://localhost:5000
VITE_APP_NAME=Flowamaz
VITE_HELP_DOCS_URL=https://docs.flowamaz.io

# Email — Transactional (Resend)
RESEND_API_KEY=
EMAIL_FROM_ADDRESS=noreply@flowamaz.io
EMAIL_FROM_NAME=Flowamaz
SUPPORT_EMAIL=support@flowamaz.io
SECURITY_EMAIL=security@flowamaz.io

# Webhook Delivery
WEBHOOK_SIGNING_SECRET_SALT=
WEBHOOK_DELIVERY_MAX_RETRIES=5

# Metering
METERING_ENABLED=true
METERING_BATCH_SIZE=100

# Library Sync (GitHub)
GITHUB_CONNECTORS_REPO=flowamaz-io/connectors
GITHUB_TEMPLATES_REPO=flowamaz-io/templates
GITHUB_APP_ID=
GITHUB_APP_PRIVATE_KEY=
LIBRARY_SYNC_INTERVAL_MINUTES=15
LIBRARY_WEBHOOK_SECRET=

# Community Edition
EDITION=community
COMMUNITY_MAX_WORKFLOWS=5
COMMUNITY_MAX_RUNS_MONTH=500
COMMUNITY_MAX_USERS=1

# Docker
COMPOSE_PROJECT_NAME=flowamaz

# Help Docs
HELP_DOCS_BASE_URL=https://docs.flowamaz.io
HELP_DOCS_GITHUB_REPO=flowamaz-io/docs
```

---

## 23. LIBRARY ECOSYSTEM

### 23.1 Concept — Two Tiers, One Ecosystem
The Library is a dedicated section inside the Flowamaz application. It is the free, community, git-backed layer. The Marketplace is the paid, curated, commercial layer on top.

| Dimension | Library | Marketplace |
|-----------|---------|------------|
| Cost to user | Free | Paid (publisher sets price) |
| Community edition | ✓ Yes | ✗ No |
| Security review | Automated CI only | Human review 3-5 days |
| Contribution method | GitHub PR (auto-merge if CI passes) | Publisher portal application |
| SLA from contributor | None required | Required (24h bug fix) |
| Source code | Public GitHub MIT | Source inspectable or proprietary |
| Revenue split | N/A | 80% contributor / 20% Flowamaz |
| GitHub source | flowamaz-io/connectors or /templates | Publisher's repo |

### 23.2 Library Component Types (Extensible)
| Type | Phase | Description |
|------|-------|-------------|
| 🔌 Connectors | Phase 4 | Integrations with external systems |
| 📋 Templates | Phase 4 | Functional YAML workflow definitions |
| 📝 Forms | Phase 5 | Reusable human gate form definitions |
| 📦 Packs | Phase 6 | Bundles (connector + templates for a domain) |
| 🖥️ Apps | Year 2 | Micro-applications built on Flowamaz workflows |
| 🤖 AI Prompts | Year 2 | Reusable AI node prompt templates |

### 23.3 What a Library Connector Contains
```
connectors/{publisher-id}/{connector-name}/
  connector.yaml     ← manifest (id, publisher, version, auth, network.allowed_domains, operations[])
  operations/        ← one .yaml per operation (input_schema, output_schema, errors, idempotency)
  src/               ← TypeScript implementation
  tests/             ← mock server tests
  README.md          ← setup guide, auth instructions, examples
  CHANGELOG.md
  LICENSE            ← MIT for community connectors
```

### 23.4 What a Library Template Contains
```
templates/{publisher-id}/{template-name}/
  template.yaml      ← manifest (id, version, required_connectors, tags, customise[])
  workflow.yaml      ← complete, valid, tested YAML
  workflow-def.md    ← plain English description (the NL template)
  tests/             ← test suite with happy/failure/edge cases
  fixtures/          ← sample input payloads for immediate dry-run
  README.md          ← setup guide, what to customise
  CHANGELOG.md
```

### 23.5 Library fmz CLI Commands
```bash
fmz library list --type connector --category finance
fmz library search "SAP purchase"
fmz library show sap-s4hana
fmz library install sap-s4hana --workspace ws_finance
fmz library install finance-team/purchase-approval --template --workspace ws_finance
fmz library outdated --workspace ws_finance
fmz library update sap-s4hana --workspace ws_finance
fmz connector init my-connector     # scaffold
fmz connector validate my-connector # validate locally
fmz connector publish my-connector  # fork → branch → PR → auto-merge → Library live
fmz template init my-template
fmz template validate my-template
fmz template publish my-template
```

### 23.6 Auto-Merge Criteria (community tier — no human review)
ALL must be true:
- All automated CI checks pass
- No Critical or High CVEs in dependencies
- network.allowed_domains contains no wildcard entries
- Connector does not claim to be Official
- Publisher has signed the CLA
- Publisher account is > 30 days old

Requires human review (do NOT auto-merge):
- Wildcard in network.allowed_domains
- Auth type is "custom"
- Cloud provider API connectors (AWS, GCP, Azure)
- Publisher account < 30 days old

### 23.7 Library GitHub Sync
- Webhook-based (immediate): fires on every push to main in connectors/templates repos
- Polling fallback (every 15 minutes): Hangfire job via GitHub API
- Sync failure: serve last known state, alert platform team after 3 retries
- Deprecation: 90-day notice, warning badge, then removal from install list

### 23.8 Connector Versioning
- Independent semver per connector in connector.yaml
- Workspace pins to major version: `publisher/connector@1`
- Minor/patch: auto-applied with changelog notification
- Major: workspace admin must explicitly upgrade
- Deprecated connectors: 90-day notice before removal

### 23.9 Phase Plan for Library
- Phase 4: Library section UI (Connectors + Templates tabs), GitHub sync service, template install flow with setup guide, fmz library commands
- Phase 5: fmz connector/template publish (auto-PR flow), contributor profiles, Library → Marketplace upgrade path
- Phase 6: Marketplace as paid tier, Packs component type, publisher portal, Stripe Connect payouts

---

## 24. GITHUB REPOSITORY STRATEGY

### 24.1 Organisation
- GitHub org: flowamaz-io (standalone — NOT under Tootker Solutions)
- Founder has org Owner role
- Tootker account can be org admin member

### 24.2 Branching Strategy (all repos)
Permanent: `main` (production), `develop` (integration)
Release: `release/vX.Y.Z` (cut from develop, bugfixes only)
Working: `feat/*`, `fix/*`, `chore/*`, `docs/*`, `connector/*`, `template/*`, `hotfix/*`
Rules: No direct push to main or develop. No merge with failing CI. No force-push to permanent branches.

### 24.3 Commit Message Standard (Conventional Commits — enforced)
Format: `type(scope): description`
Types: feat, fix, chore, docs, test, refactor, perf, security, connector, template
Scopes: engine, cli, canvas, connector-sdk, sdk, vscode, community, docs, infra, platform

### 24.4 Semantic Versioning (public monorepo only)
- BREAKING CHANGE → major (X.0.0)
- feat → minor (X.Y.0)
- fix/chore/docs → patch (X.Y.Z)
- Each tag triggers: npm publish, Docker push, GitHub Release, binary builds, Homebrew update

### 24.5 CLA Required
All contributors must sign the Flowamaz CLA before any PR is merged.
Automated via CLA Assistant GitHub App.
Grants Flowamaz right to use contributions in both MIT open source and commercial product.

### 24.6 Platform → Engine Dependency
Platform repo references public packages as npm dependencies (never git submodule or source import).
Platform always uses a released version. This enforces the public API contract.

---

## 25. DEFINITION OF DONE

### Per Prompt
- [ ] All declared output files exist on disk
- [ ] dotnet build — zero errors, zero warnings
- [ ] Vue TypeScript compiles — zero errors
- [ ] Unit tests written for all new service methods
- [ ] All unit tests pass
- [ ] Service layer coverage ≥ 80%
- [ ] Serilog entry/exit/error on all new functions (backend)
- [ ] Zero style blocks in any Vue component
- [ ] Scalar accessible at /scalar (backend scaffold prompts)
- [ ] Workspace isolation enforced (workspace_id in all scoped queries)
- [ ] Credential values never in logs
- [ ] AI calls metered (if any AI calls in this prompt)
- [ ] Every empty state designed — not just happy path
- [ ] Every error state shows actionable message
- [ ] Shallow verification passed
- [ ] Result logged to results.md
- [ ] /clear executed

### Per Phase
- [ ] All prompts completed or skipped with documented reason
- [ ] Verifier deep review complete
- [ ] UX review complete (if applicable)
- [ ] UI review complete (if applicable)
- [ ] Testing Agent complete — backend + web layers
- [ ] Security Agent complete — static + Trivy (ZAP: skipped until staging URL declared)
- [ ] Phase report compiled and written to project root
- [ ] Desktop notification fired
- [ ] Phase report ready for Chat upload

---

*This document is the single source of truth. If anything in a phase prompt contradicts this document, this document wins. If an Executor is uncertain, read this document. If a Verifier finds a violation of anything in this document, it is Critical.*
