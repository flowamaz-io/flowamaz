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
