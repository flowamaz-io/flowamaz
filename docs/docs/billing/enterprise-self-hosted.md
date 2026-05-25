---
title: Enterprise Self-Hosted
updated: 2026-05-24
readingTime: "3 min read"
plans: "Enterprise"
---

# Enterprise Self-Hosted

Enterprise Self-Hosted runs Flowamaz entirely on your own infrastructure, under an
annual licence. It's built for organisations that can't or won't put their data in a
shared cloud.

## Who it's for

- **Regulated industries** — finance, healthcare, government, and others with strict
  compliance obligations.
- **Data sovereignty requirements** — when data must stay within a specific region or
  network.
- **Air-gapped or private environments** — where the platform must run without depending
  on an external SaaS.

## What's included

Everything in the Pro plan, plus:

- **CMEK** — customer-managed encryption keys, so you hold the keys to your own data.
- **Data residency** — full control over where data lives and is processed.
- **Bring-your-own-model (BYOM)** — point AI nodes at any OpenAI-compatible endpoint,
  including private/in-house models.
- **Dedicated support** — a direct line to the Flowamaz team for deployment and
  operations.
- **Unlimited** workflows, runs, workspaces, and members.

## Deployment requirements

- **Docker** (with Docker Compose) on a Linux host.
- Recommended minimum: 4 vCPUs, 8 GB RAM, and 50 GB of disk for a single-node start;
  size up for higher run volumes.
- PostgreSQL and Redis (bundled in the deployment, or point at your own managed
  instances).
- Outbound network access only for the connectors and AI providers you choose to use.

## Licence keys

Enterprise runs on an **annual licence key**. The key unlocks Enterprise features and is
renewed each year. If a licence lapses, the engine keeps your data safe and intact;
renew the key to restore full functionality.

## How to get a quote

Enterprise is priced per deployment. Email **team@flowamaz.io** with your expected scale
(workflows, runs per month, number of users) and any compliance requirements, and we'll
prepare a quote and a deployment plan.

For a feature-by-feature view of all tiers, see
[Plans comparison](../getting-started/plans-comparison).

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/billing/enterprise-self-hosted.md)
