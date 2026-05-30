---
title: Triggering Workflows
updated: 2026-05-30
readingTime: "3 min read"
plans: "All plans"
---

# Triggering Workflows

An **instance** is one execution of a workflow. There are four ways to start one,
all of which run the published production version (unless you start a test run).

## Manual

Open the workflow and click **Run**. You can supply an input payload as JSON, then
watch the instance execute step by step. **Use it** for one-off runs and while
testing.

## Webhook

Each workflow can expose an inbound webhook endpoint. An external system POSTs to the
URL and Flowamaz starts an instance with the request body as input.

```http
POST /api/v1/webhooks/{endpoint-id}
Content-Type: application/json
X-Flowamaz-Signature: sha256=...

{ "amount": 7500, "requester": "alex" }
```

Webhooks are HMAC-signed and idempotent. **Use it** to trigger from third-party
services like Stripe, GitHub, or your own apps. See **Webhooks** for setup.

## Public API

Trigger programmatically with a workspace API key:

```http
POST /api/v1/workflows/{id}/instances
Authorization: Bearer fmz_live_...
Content-Type: application/json

{ "input": { "amount": 7500 } }
```

The key needs the `workflows:trigger` scope. **Use it** for CI/CD, scripts, and
backend integrations.

## Schedule

Attach a schedule to a workflow's Trigger node to run it automatically on a cron-style
cadence — hourly, nightly, weekly, and so on. The scheduler starts a fresh instance
each time it fires. **Use it** for recurring jobs like reports or syncs.

## After triggering

However it started, every instance appears in the **Instances** list where you can
watch its timeline, inspect variables, and see its status in real time.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/instances/triggering-workflows.md)
