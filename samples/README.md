# Samples

## Loan Approval (`loan-approval/`) and the mock loan application (`loan-app-mock/`)

A worked example of an external application running its business process on Flowamaz through the
**public API**. Today the "application" is a mock with no UI. The real loan application will make
the same API calls.

### The process

```
Form submitted ─► Credit review ─► Level 1 (Branch Manager)
                                   ├─ ≤ 50K ─────────────────────────────► Create Letter of Offer
                                   └─ > 50K ─► Level 2 (Credit Manager)
                                               ├─ ≤ 250K ────────────────► Create Letter of Offer
                                               └─ > 250K ─► Level 3 (Credit Committee) ─► Create Letter of Offer
Create Letter of Offer ─► Customer accepts? ─► yes: notify "offer_accepted"  ─► Loan accepted
                                             └► no / 7 days: "offer_declined" ─► Offer declined
Any review/approval rejection ─► notify "application_rejected" ─► Application rejected
```

- **Approvals are cumulative:** a 400K loan needs Levels 1, 2 and 3. The limits are inclusive, so
  50,000 stays at Level 1 and 250,000 stays at Level 2.
- **Definition:** `loan-approval/loan-approval.workflow.yaml`, a flowamaz/v1 definition. Paste it
  into a workflow's YAML tab, or build it on the canvas.
- **Letter of Offer:** Flowamaz calls the loan application (`POST /letters-of-offer`), which
  creates the letter and returns its number.
- **Outcomes:** the loan application is notified of every outcome (`POST /events`).

### How the loan application talks to Flowamaz

All calls use a workspace API key (`Authorization: Bearer fmz_…`), created in Settings → API keys
with the scopes **workflows:read, workflows:trigger, instances:read, instances:write**.

| Step in the loan app | Public API call |
|---|---|
| Find the workflow | `GET /api/public/v1/workflows` → the `slug` of "Loan Approval" |
| Customer submits the form | `POST /api/public/v1/workflows/{slug}/trigger` with the form JSON as the body (`X-Idempotency-Key: <application id>` prevents double submits) → `instance_id` |
| Show the reviewer or approver their queue | `GET /api/public/v1/gates/pending` → each item has `id`, `instance_id`, `node_id` (`credit-review`, `l1-approval`, `l2-approval`, `l3-approval`, `lo-acceptance`) |
| Reviewer, approver or customer decides | `POST /api/public/v1/gates/{id}/decide` with `{ "decision": "approved" \| "rejected", "note": "…" }` |
| Track progress | `GET /api/public/v1/instances/{id}` (status) and `GET /api/public/v1/instances/{id}/events` (Server-Sent Events: the steps taken) |
| Withdraw an application | `POST /api/public/v1/instances/{id}/cancel` |

Form fields (the trigger payload): `application_id`, `applicant_name`, `applicant_email`,
`loan_amount` (number), `tenure_months` (number), `purpose`, `monthly_income`.

### Run the mock

Node 20+ is needed; there are no dependencies to install.

```bash
# 1. Flowamaz running locally (prompts/e2e-chrome/start-local.sh), with Loan Approval published.
# 2. Put the API key in samples/loan-app-mock/.env.local:
#      FLOWAMAZ_URL=http://localhost:8307
#      FLOWAMAZ_API_KEY=fmz_...
node samples/loan-app-mock/server.mjs          # terminal 1: receives LO creation + events on :4010
node samples/loan-app-mock/run-scenarios.mjs   # terminal 2: 8 scenarios, prints PASS/FAIL per scenario
node samples/loan-app-mock/run-scenarios.mjs C F   # only some scenarios
```

The workflow calls `http://localhost:4010`, which works for local development, where connector
egress to private addresses is allowed. On a Cloud deployment, point the two URLs at the real loan
application.
