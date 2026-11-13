# Flowamaz — what is left after the Phase 1–8 build (13 Nov 2026)

What FUNCTIONAL.md still describes that the code does not do, sized so it can be picked from. Sizes are rough:
**S** about one PR, **M** 1–2 PRs, **L** 2–4 PRs, **XL** a project of its own. "Needs you" means a decision, an
account or an outside party has to come first; code alone cannot finish it.

## A. In-scope gaps (Phases 4–6 of the spec, not built)

Found while writing this plan, after the testing session was closed. They belong to the current product, not the
roadmap, so they come first.

| # | Item | Spec | What is missing | Size |
|---|------|------|-----------------|------|
| A1 | `fmz library` commands | §23.5, CLI | The CLI calls `/api/v1/library/{connectors,templates}[/…]` (search, show, install, outdated, update, publish). No such routes exist; the app uses `/api/v1/templates` and `/workspaces/{id}/connectors`. Every `fmz library` command except the local `init`/`validate` fails. | M |
| A2 | Connector versioning | §23.8 | Semver per connector exists, but no pinning to a major (`publisher/connector@1`), no auto-applied minor/patch with a changelog notice, no explicit major upgrade, no 90-day deprecation notice. Needed for A1's `outdated`/`update`. | M |
| A3 | Library **Forms** | §23.2 (Phase 5) | Reusable human-gate form definitions. The Library tab shows "Soon"; nothing behind it. | M |
| A4 | Library **Packs** | §23.2, §23.9 (Phase 6) | Bundles of connectors + templates for a domain, installable in one go. The tab shows "Soon". | M |

## B. Year 2

| # | Item | Spec | What it would take | Size | Needs you |
|---|------|------|--------------------|------|-----------|
| B1 | Workflow Twin — shadow simulation | §8.1 | Run a workflow version against generated input variations (boundary values from router conditions, missing/empty fields, past payloads) with connectors and gates simulated from recorded outputs, never live. Report inputs that break it as a health badge ("breaks when amount = $5,000: router uses > not ≥"). The engine and recorded I/O snapshots exist; the simulator, input generator and badge are new. | L | — |
| B2 | Workflow Memory | §8.3 | Per-workflow learning from runs: fastest approvers, inputs that usually get rejected, SLA probability, shown during execution. Can be built now as deterministic statistics on run events; it only becomes useful once there are months of real runs. | M | Real usage first |
| B3 | Workflow Negotiator | §8.5 | Detect conflicting values for the same field from two connector steps, try business rules, escalate to a gate with a recommendation. The spec is two sentences: what counts as "the same data" and where rules live needs a design first. | L | Design decision |
| B4 | Workflow Consciousness | §8.11 | Flag low-confidence AI output (partly there: visual, conversation and SOP confidence), detect connector behaviour changes (response-shape drift) before failure, six-month-stale workflow alerts, regulatory alerts ("predates PDPA 2024"). Stale alerts and drift detection are code; regulatory alerts need a maintained rules source. | M (L with regulatory) | Regulatory content source |
| B5 | Real-time canvas collaboration | §20 | Several people editing one workflow: presence, cursors, conflict-free edits (a CRDT such as Yjs over the existing WebSocket), save/commit semantics with Git versioning. | L | — |
| B6 | CMEK — customer-managed keys | §12.8 | Envelope encryption of the credential vault with a per-org key held in the customer's AWS KMS / GCP KMS / Azure Key Vault / HashiCorp Vault; crypto-shredding by revoking the key. Enterprise only. | L | Cloud KMS accounts to test against |
| B7 | Multi-language | §3.2, §6.5, §20 | vue-i18n strings beyond English; NL and SOP input in other languages (prompts already accept any text; output language and UI copy are the work). | M | Which languages; translations |
| B8 | Mobile companion app (Flutter) | §3.4, §20 | New app (Riverpod, Dio, GoRouter): sign-in, approvals inbox with approve/reject, run status, push notifications. The public API and gate endpoints exist. Store accounts and push credentials required. | XL | Apple/Google developer accounts |
| B9 | Library **Apps** and **AI Prompts** | §23.2 | Micro-applications on top of workflows; reusable AI-node prompt templates. AI Prompts is small; Apps needs a product definition. | S + L | Apps: product definition |

## C. Year 3 and later

| # | Item | Spec | What it would take | Size | Needs you |
|---|------|------|--------------------|------|-----------|
| C1 | Workflow Archaeology / process mining | §8.6 | Ingest months of email/ERP/Slack history through connectors, mine event logs into a process model, render it on the canvas. Needs broad connector depth and customer data access. | XL | Customer data + consent |
| C2 | Multi-agent orchestration (AI→AI chains) | §20 | AI nodes that hand work to other AI nodes with shared context, budgets and stop conditions; metering per chain. | M–L | — |
| C3 | Cross-workspace orchestration | §20 | A workflow in one workspace triggering or waiting on one in another. Conflicts with rule 1 (workspace isolation): needs an explicit, audited grant model before any code. | M | Security design sign-off |
| C4 | Data residency (3 regions) | §12.9, §20 | Per-org region pinning (eu-west-1, ap-southeast-1, US), separate databases/storage per region, routing at sign-in. Mostly infrastructure and operations. | XL | Hosting in each region |
| C5 | Compliance: SOC 2, ISO 27001, HIPAA BAA, FedRAMP | §12.9 | Certifications by outside auditors. Code helps (audit log, encryption, access control already exist) but the work is policies, evidence collection and audits over months. Not something a PR closes. | — | Auditor, budget, time |

## Suggested order

1. **A1–A4** (one or two PRs): they are promised by the current spec and A1 is visibly broken in the CLI.
2. **B1 Twin** and **B4 Consciousness** (stale alerts + drift): the most product value for code alone.
3. **B5 collaboration**, **B7 multi-language**: broaden who can use it.
4. **B2 Memory** once real runs accumulate; **B3 Negotiator** after a short design.
5. **B6 CMEK**, **B8 mobile**, Year 3 items when there are customers asking and the accounts/infrastructure exist.
