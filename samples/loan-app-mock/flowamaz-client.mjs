// Minimal client for the Flowamaz public API (https://docs.flowamaz.io/api).
// Auth: a workspace API key (Settings → API keys) with the scopes
//   workflows:read, workflows:trigger, instances:read, instances:write
// Responses are { data, meta }; errors are { error: { code, message, docs } }.

export class FlowamazClient {
  constructor({ baseUrl, apiKey }) {
    if (!apiKey) throw new Error('FLOWAMAZ_API_KEY is not set. Create a key in Settings → API keys (see README).');
    this.baseUrl = baseUrl.replace(/\/$/, '');
    this.apiKey = apiKey;
  }

  async #call(method, path, body, headers = {}) {
    const res = await fetch(`${this.baseUrl}/api/public/v1${path}`, {
      method,
      headers: { Authorization: `Bearer ${this.apiKey}`, 'Content-Type': 'application/json', ...headers },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    const text = await res.text();
    let json;
    try { json = text ? JSON.parse(text) : {}; } catch { json = { raw: text }; }
    if (!res.ok) {
      const err = json.error ?? json;
      const e = new Error(`${method} ${path} → HTTP ${res.status} ${err.code ?? ''}: ${err.message ?? text}`);
      e.status = res.status;
      e.code = err.code;
      throw e;
    }
    return json.data ?? json;
  }

  listWorkflows() { return this.#call('GET', '/workflows'); }
  /** Starts a run. The payload becomes the workflow's variables (the "form"). */
  trigger(slug, payload, idempotencyKey) {
    return this.#call('POST', `/workflows/${encodeURIComponent(slug)}/trigger`, payload,
      idempotencyKey ? { 'X-Idempotency-Key': idempotencyKey } : {});
  }
  getInstance(id) { return this.#call('GET', `/instances/${id}`); }
  /** The run's execution events. The endpoint streams Server-Sent Events ("data: {json}" lines). */
  async getEvents(id) {
    const res = await fetch(`${this.baseUrl}/api/public/v1/instances/${id}/events`, {
      headers: { Authorization: `Bearer ${this.apiKey}`, Accept: 'text/event-stream' },
    });
    const text = await res.text();
    if (!res.ok) throw new Error(`GET /instances/${id}/events → HTTP ${res.status}: ${text.slice(0, 300)}`);
    return text.split(/\r?\n/).filter((l) => l.startsWith('data: ')).map((l) => JSON.parse(l.slice(6)));
  }
  pendingGates() { return this.#call('GET', '/gates/pending'); }
  /** decision: "approved" | "rejected" */
  decide(gateId, decision, note) { return this.#call('POST', `/gates/${gateId}/decide`, { decision, note }); }
  cancel(id) { return this.#call('POST', `/instances/${id}/cancel`); }
}
