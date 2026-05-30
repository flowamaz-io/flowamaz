---
title: Webhooks
updated: 2026-05-30
readingTime: "4 min read"
plans: "All plans"
---

# Webhooks

A webhook is an inbound endpoint that triggers a workflow when an external system
POSTs to it. Use webhooks to start workflows from Stripe, GitHub, your own apps, or any
service that can send an HTTP request.

## Creating an endpoint

1. Open **Settings → Webhooks** (or the workflow's Trigger node).
2. Click **Create endpoint** and pick the workflow to trigger.
3. Copy the generated URL and the **signing secret** — the secret is shown once.

Point your external service at the URL. Each request body becomes the instance's input
payload.

```http
POST /api/v1/webhooks/{endpoint-id}
Content-Type: application/json
X-Flowamaz-Signature: sha256=...
X-Idempotency-Key: evt_8f3a...

{ "amount": 7500, "requester": "alex" }
```

## HMAC signing

Every inbound request is verified with an HMAC signature so only your trusted sender
can trigger the workflow. Flowamaz computes an HMAC-SHA256 of the raw request body
using the signing secret and compares it to the `X-Flowamaz-Signature` header.

- Configure your sender to sign each payload with the same secret.
- Requests with a missing or invalid signature are rejected before any workflow runs.
- Rotate the secret from the endpoint settings if it's ever exposed.

## Idempotency

Senders often retry on network hiccups, which can deliver the same event twice.
Flowamaz makes delivery **idempotent**: include an `X-Idempotency-Key` header with a
unique value per event.

- The first request with a given key triggers an instance.
- Any later request with the **same key** is acknowledged but does **not** start a
  second instance.

This guarantees one event produces exactly one run, even if your sender retries.

## Troubleshooting

- **401 / signature errors** — the secret on your sender doesn't match Flowamaz, or
  the body was modified in transit. Re-copy the secret and sign the raw body.
- **Duplicate runs** — your sender isn't sending a stable idempotency key.
- **No instance created** — check the endpoint is enabled and the workflow is
  published.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/settings/webhooks.md)
