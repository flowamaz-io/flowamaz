# Feature Depth Audit — 2026-10-23 (code read, not phase reports)

Input for test-03 triage. Classifications: REAL / PARTIAL / SHALLOW (canned, or LLM-only) / MISSING.

## Headline — connectors cannot be used as workflow steps
- Connector handlers are only invoked from HumanGateNodeWorker (Slack/Teams approval messages).
- Action nodes run via HttpActionWorker: raw HTTP from node config, no connector_id dispatch,
  no credential vault lookup. ConnectorSandbox is registered in DI but never called.
- Seeded connector manifests have "operations": [] (ConnectorSeedData.cs).
- OAuth start/callback (Slack, GitHub, Teams) stores tokens in the vault, but no handler reads them.

| Connector | State |
|---|---|
| HTTP/REST, PostgreSQL, Email (Resend), Webhook emit, Webhook receive | Real calls; creds from node input, not vault |
| Slack | Bearer "placeholder" (SlackConnectorHandler.cs:74,112,163) — always fails auth |
| GitHub | SetAuth(request, null) (GitHubConnectorHandler.cs:64,90) — unauthenticated |
| Teams | Incoming-webhook URL only, not Graph |
| Schedule/Cron | Validation only; no Quartz job fires Schedule triggers |
| MySQL | Stub returns "not available" |
| File system, Script/Shell, Microsoft 365 | MISSING (seed rows only) |

## AI stub hazard
AiCompletionService returns canned output without throwing when no key / UseStubCompletion
(echo of prompt; fixed 3-node graph for images). WorkflowCreationController saves generated YAML
without checking IsValid → without a key, NL/voice/conversation/SOP save invalid workflows and
visual always yields the same 3 nodes. APP-BUG: reject invalid YAML at save.

## Creation methods (§6)
| Method | Class | Gap |
|---|---|---|
| NL | PARTIAL | No clarifying questions; invalid YAML saved |
| Voice | PARTIAL | Voice tab (NewWorkflowView.vue:147) VoiceInputButton has no transcript handler — APP-BUG; works only inside NL form |
| Visual | SHALLOW | Single vision call; drops <80% nodes instead of asking; image not stored |
| Conversation | SHALLOW | Extraction LLM call → NL generator; empty DTO on parse fail |
| SOP | PARTIAL | Real PDF/DOCX extraction; no per-section confidence |
| Canvas | PARTIAL | No box select, minimap not interactive, no node→YAML jump, no grid snap |
| Co-pilot | PARTIAL | Real regex pattern matcher; rest is LLM |

## Magic features scheduled Phase 1–6
| Feature | Class |
|---|---|
| Weather (8.7) | REAL |
| Empathy (8.2) | PARTIAL — graph heuristics, not instance events; not persisted |
| DNA (8.8) | PARTIAL — fingerprint real; UI uses name-overlap clone suggestion, no inheritance |
| Interpreter (8.10) | PARTIAL — Auditor/Developer real; CEO narrative LLM-only; .md download |
| Conversation→Workflow (8.4) | SHALLOW |
| Economy (8.9) | MISSING — no pricing / revenue share / Stripe Connect |
Year 2/3 features (Twin, Memory, Negotiator, Archaeology, Consciousness): not present.

## Triage guidance
- Treat connector-as-step, Schedule triggers, and the three missing connectors as NOT-BUILT
  (human scope decision), not test bugs.
- Slack placeholder token, GitHub null auth, invalid-YAML save, dead voice tab: APP-BUG, High.
- E2E must never depend on a live AI key; assert stub mode where AI is involved.

---

## Status after PR #2 (fix/audit-findings, 2026-10-23)

Closed:
- Connectors run as workflow steps (connector_id/operation/inputs/credential), vault credentials
  resolved + decrypted per call, OAuth refresh for Microsoft. Slack/GitHub auth fixed; Teams via
  Graph; MySQL, File system, Microsoft 365 implemented; Script/Shell implemented but OFF by default.
- Schedule triggers fire (exactly-once per occurrence).
- Invalid generated YAML is rejected (422); AI without a key fails loudly in production.
- Voice tab works; credential wizard stores API keys; Settings → Credentials added.
- Found and fixed beyond the audit: approval gates never ran in the real worker loop; routers took
  every branch; numeric/boolean YAML config was read as strings; canvas AI/gate/trigger keys were
  ignored; UI variable syntax (${x}) never resolved; trigger payload never became variables;
  revoked credentials still decrypted; SSRF to internal addresses; Email HTML injection; YAML
  injection in canvas skeleton; Microsoft OAuth URL missing redirect_uri/scope; build broken by
  new NuGet advisories.

Still open (scope decisions, not bugs):
- Connector sandbox process isolation (§9.5) — in-process egress guard only; Script/Shell must stay
  disabled on Cloud until this exists.
- Workflow Economy (§8.9), canvas interactions (box select, clickable minimap, node→YAML jump, grid
  snap), Visual input asking about low-confidence nodes, NL clarifying questions, gate onTimeout
  auto-approve/auto-reject, a Slack channel field and Teams webhook field in the gate panel.
- Rate limiting / abuse controls for the platform Email connector (sends from the platform domain).
