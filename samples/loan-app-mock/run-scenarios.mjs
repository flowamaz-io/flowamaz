// Drives the "Loan Approval" workflow through the Flowamaz public API, acting as the loan
// application: submits applications (the form), makes the review / approval / customer decisions,
// and checks every run takes exactly the expected path and calls the mock server back.
//
//   node run-scenarios.mjs                # all scenarios
//   node run-scenarios.mjs C F            # only some
//
// Env (or ./.env.local): FLOWAMAZ_URL (default http://localhost:8307), FLOWAMAZ_API_KEY,
// WORKFLOW_SLUG (default: found by the name "Loan Approval"), MOCK_URL (default http://localhost:4010).
import { existsSync, readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { FlowamazClient } from './flowamaz-client.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const envFile = join(HERE, '.env.local');
if (existsSync(envFile)) {
  for (const line of readFileSync(envFile, 'utf8').split(/\r?\n/)) {
    const m = line.match(/^\s*([A-Z_]+)\s*=\s*(.*)\s*$/);
    if (m && !process.env[m[1]]) process.env[m[1]] = m[2].replace(/^["']|["']$/g, '');
  }
}

const FLOWAMAZ_URL = process.env.FLOWAMAZ_URL ?? 'http://localhost:8307';
const MOCK_URL = process.env.MOCK_URL ?? 'http://localhost:4010';
const client = new FlowamazClient({ baseUrl: FLOWAMAZ_URL, apiKey: process.env.FLOWAMAZ_API_KEY });
const RUN = Date.now().toString(36).toUpperCase();
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// Each step: which approval should be waiting next, and what the loan application decides.
const A = (gate) => ({ gate, decision: 'approved' });
const R = (gate) => ({ gate, decision: 'rejected' });
const SCENARIOS = [
  { id: 'A', title: '30K — Level 1 only, customer accepts', amount: 30_000,
    steps: [A('credit-review'), A('l1-approval'), A('lo-acceptance')], end: 'end-accepted', lo: true, event: 'offer_accepted' },
  { id: 'B', title: '120K — Levels 1+2, customer accepts', amount: 120_000,
    steps: [A('credit-review'), A('l1-approval'), A('l2-approval'), A('lo-acceptance')], end: 'end-accepted', lo: true, event: 'offer_accepted' },
  { id: 'C', title: '400K — Levels 1+2+3, customer accepts', amount: 400_000,
    steps: [A('credit-review'), A('l1-approval'), A('l2-approval'), A('l3-approval'), A('lo-acceptance')], end: 'end-accepted', lo: true, event: 'offer_accepted' },
  { id: 'D', title: '90K — rejected at credit review', amount: 90_000,
    steps: [R('credit-review')], end: 'end-rejected', lo: false, event: 'application_rejected' },
  { id: 'E', title: '180K — rejected at Level 2', amount: 180_000,
    steps: [A('credit-review'), A('l1-approval'), R('l2-approval')], end: 'end-rejected', lo: false, event: 'application_rejected' },
  { id: 'F', title: '75K — customer declines the offer', amount: 75_000,
    steps: [A('credit-review'), A('l1-approval'), A('l2-approval'), R('lo-acceptance')], end: 'end-declined', lo: true, event: 'offer_declined' },
  { id: 'G', title: 'Exactly 50K — boundary stays at Level 1', amount: 50_000,
    steps: [A('credit-review'), A('l1-approval'), A('lo-acceptance')], end: 'end-accepted', lo: true, event: 'offer_accepted' },
  { id: 'H', title: 'Exactly 250K — boundary stays at Level 2', amount: 250_000,
    steps: [A('credit-review'), A('l1-approval'), A('l2-approval'), A('lo-acceptance')], end: 'end-accepted', lo: true, event: 'offer_accepted' },
];

async function findSlug() {
  if (process.env.WORKFLOW_SLUG) return process.env.WORKFLOW_SLUG;
  const list = await client.listWorkflows();
  const items = Array.isArray(list) ? list : list.items ?? [];
  const wf = items.find((w) => w.name === 'Loan Approval' && w.status === 'Published')
    ?? items.find((w) => /loan/i.test(w.name) && w.status === 'Published');
  if (!wf) throw new Error('No published "Loan Approval" workflow found. Build and publish it first (Chrome journey J18), or set WORKFLOW_SLUG.');
  return wf.slug;
}

async function waitForGate(instanceId, timeoutMs = 30_000) {
  const until = Date.now() + timeoutMs;
  while (Date.now() < until) {
    const pending = await client.pendingGates();
    const gate = (Array.isArray(pending) ? pending : []).find((g) => g.instance_id === instanceId);
    if (gate) return gate;
    const inst = await client.getInstance(instanceId);
    if (['Completed', 'Failed', 'Cancelled'].includes(inst.status)) return { finished: inst.status };
    await sleep(700);
  }
  return null;
}

async function waitForEnd(instanceId, timeoutMs = 30_000) {
  const until = Date.now() + timeoutMs;
  while (Date.now() < until) {
    const inst = await client.getInstance(instanceId);
    if (['Completed', 'Failed', 'Cancelled'].includes(inst.status)) return inst;
    await sleep(700);
  }
  return client.getInstance(instanceId);
}

async function mockState() {
  const res = await fetch(`${MOCK_URL}/state`);
  if (!res.ok) throw new Error(`Mock server not reachable at ${MOCK_URL} — start it with: node server.mjs`);
  return res.json();
}

async function runScenario(slug, s) {
  const checks = [];
  const check = (name, ok, detail = '') => checks.push({ name, ok, detail });
  const applicationId = `APP-${RUN}-${s.id}`;
  const form = {
    application_id: applicationId,
    applicant_name: `Applicant ${s.id}`,
    applicant_email: `applicant.${s.id.toLowerCase()}@example.com`,
    loan_amount: s.amount,
    tenure_months: 60,
    purpose: 'Business expansion',
    monthly_income: 18_000,
  };

  const started = await client.trigger(slug, form, applicationId);
  const instanceId = started.instance_id;
  check('application submitted (trigger accepted)', Boolean(instanceId), instanceId);

  for (const step of s.steps) {
    const gate = await waitForGate(instanceId);
    if (!gate || gate.finished) {
      check(`waiting at ${step.gate}`, false, gate?.finished ? `run already ${gate.finished}` : 'no approval appeared within 30s');
      break;
    }
    check(`waiting at ${step.gate}`, gate.node_id === step.gate, `actual: ${gate.node_id}`);
    if (gate.node_id !== step.gate) break;
    await client.decide(gate.id, step.decision, `mock loan app: ${step.decision}`);
  }

  const inst = await waitForEnd(instanceId);
  check('run completed', inst.status === 'Completed', `status: ${inst.status}`);

  const events = await client.getEvents(instanceId);
  const list = Array.isArray(events) ? events : events.items ?? [];
  const visited = new Set(list.filter((e) => e.node_id).map((e) => e.node_id));
  check(`ended at ${s.end}`, visited.has(s.end), `ends seen: ${[...visited].filter((n) => n.startsWith('end-')).join(', ') || 'none'}`);
  const skipped = ['l1-approval', 'l2-approval', 'l3-approval'].filter((g) => !s.steps.some((st) => st.gate === g));
  const wronglyVisited = skipped.filter((g) => visited.has(g));
  check('no approval level beyond the limit was opened', wronglyVisited.length === 0, wronglyVisited.join(', '));

  const state = await mockState();
  const lo = state.letters_of_offer.find((l) => l.application_id === applicationId);
  check(s.lo ? 'Letter of Offer created' : 'no Letter of Offer created', s.lo ? Boolean(lo) : !lo, lo?.lo_number ?? '');
  if (lo) check('LO carries the applied amount', lo.amount === s.amount, `${lo.amount}`);
  const evt = state.events.filter((e) => e.application_id === applicationId).map((e) => e.event);
  check(`loan app notified: ${s.event}`, evt.length === 1 && evt[0] === s.event, evt.join(', ') || 'none');

  return { ...s, applicationId, instanceId, loNumber: lo?.lo_number, checks, pass: checks.every((c) => c.ok) };
}

async function main() {
  const only = process.argv.slice(2).map((a) => a.toUpperCase());
  const scenarios = only.length ? SCENARIOS.filter((s) => only.includes(s.id)) : SCENARIOS;
  await mockState(); // fail fast if the mock server isn't running
  const slug = await findSlug();
  console.log(`Flowamaz ${FLOWAMAZ_URL} · workflow "${slug}" · mock ${MOCK_URL} · run ${RUN}\n`);

  const results = [];
  for (const s of scenarios) {
    process.stdout.write(`${s.id}  ${s.title} … `);
    try {
      const r = await runScenario(slug, s);
      results.push(r);
      console.log(r.pass ? 'PASS' : 'FAIL');
      for (const c of r.checks.filter((c) => !c.ok)) console.log(`     ✗ ${c.name} (${c.detail})`);
    } catch (e) {
      results.push({ ...s, pass: false, error: e.message, checks: [] });
      console.log(`ERROR\n     ${e.message}`);
    }
  }

  const passed = results.filter((r) => r.pass).length;
  console.log(`\n${passed}/${results.length} scenarios passed`);
  const out = join(HERE, 'data');
  mkdirSync(out, { recursive: true });
  writeFileSync(join(out, `results-${RUN}.json`), JSON.stringify({ run: RUN, slug, results }, null, 2));
  console.log(`Details: ${join('samples', 'loan-app-mock', 'data', `results-${RUN}.json`)}`);
  process.exit(passed === results.length ? 0 : 1);
}

main().catch((e) => { console.error(e.message); process.exit(2); });
