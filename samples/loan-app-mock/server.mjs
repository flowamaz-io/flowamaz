// Mock loan application — the systems side the Loan Approval workflow calls back into.
//   POST /letters-of-offer   create a Letter of Offer, returns { lo_number }
//   POST /events             record a workflow outcome { application_id, event }
//   GET  /state              everything received (used by run-scenarios.mjs to verify)
//   GET  /health
// No UI and no database: state lives in memory and in ./data/state.json for inspection.
import { createServer } from 'node:http';
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const PORT = Number(process.env.MOCK_PORT ?? 4010);
const DATA = join(dirname(fileURLToPath(import.meta.url)), 'data');
mkdirSync(DATA, { recursive: true });

const state = { letters_of_offer: [], events: [] };
let loSequence = 0;

const save = () => writeFileSync(join(DATA, 'state.json'), JSON.stringify(state, null, 2));
const send = (res, status, body) => {
  res.writeHead(status, { 'Content-Type': 'application/json' });
  res.end(JSON.stringify(body));
};
const readJson = (req) =>
  new Promise((resolve, reject) => {
    let raw = '';
    req.on('data', (c) => (raw += c));
    req.on('end', () => {
      try { resolve(raw ? JSON.parse(raw) : {}); } catch (e) { reject(new Error(`Body is not JSON: ${raw.slice(0, 200)}`)); }
    });
  });

const server = createServer(async (req, res) => {
  const stamp = new Date().toISOString();
  try {
    if (req.method === 'GET' && req.url === '/health') return send(res, 200, { status: 'ok' });
    if (req.method === 'GET' && req.url === '/state') return send(res, 200, state);

    if (req.method === 'POST' && req.url === '/letters-of-offer') {
      const body = await readJson(req);
      if (!body.application_id || typeof body.amount !== 'number')
        return send(res, 400, { error: 'application_id and a numeric amount are required' });
      loSequence += 1;
      const lo = {
        lo_number: `LO-${new Date().getFullYear()}-${String(loSequence).padStart(4, '0')}`,
        application_id: body.application_id,
        applicant_name: body.applicant_name,
        amount: body.amount,
        tenure_months: body.tenure_months,
        created_at: stamp,
      };
      state.letters_of_offer.push(lo);
      save();
      console.log(`[${stamp}] LO created ${lo.lo_number} for ${lo.application_id} (${lo.amount})`);
      return send(res, 201, lo);
    }

    if (req.method === 'POST' && req.url === '/events') {
      const body = await readJson(req);
      if (!body.application_id || !body.event) return send(res, 400, { error: 'application_id and event are required' });
      state.events.push({ ...body, received_at: stamp });
      save();
      console.log(`[${stamp}] event ${body.event} for ${body.application_id}`);
      return send(res, 200, { received: true });
    }

    return send(res, 404, { error: `No route for ${req.method} ${req.url}` });
  } catch (e) {
    console.error(`[${stamp}] ${req.method} ${req.url} failed: ${e.message}`);
    return send(res, 400, { error: e.message });
  }
});

server.listen(PORT, () => console.log(`Mock loan application listening on http://localhost:${PORT}`));
