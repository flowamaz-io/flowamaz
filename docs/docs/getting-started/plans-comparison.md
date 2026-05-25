---
title: Plans Comparison
updated: 2026-05-24
readingTime: "4 min read"
plans: "All plans"
---

# Plans Comparison

Flowamaz has four tiers. Community is free and self-hosted; Starter, Pro, and
Enterprise are Cloud plans. Here is exactly what each one includes.

## Limits at a glance

| Limit | Community | Starter | Pro | Enterprise |
|-------|-----------|---------|-----|-----------|
| Workflows | 5 | 20 | Unlimited | Unlimited |
| Runs / month | 500 | 5,000 | 50,000 | Custom |
| Workspaces | 1 | 3 | 10 | Unlimited |
| Members / workspace | 1 | 10 | 50 | Unlimited |
| Storage | 1 GB | 10 GB | 50 GB | Custom |
| Run retention | 7 days | 90 days | 365 days | Custom |
| API rate limit | 10 / min | 60 / min | 300 / min | Custom |
| AI models | Anthropic only | + Azure + Google | All | All + BYOM |

## What each limit means in practice

- **Workflows** — the number of distinct workflow definitions you can keep. Drafts
  count once published.
- **Runs / month** — each time a workflow is triggered, that's one run. A workflow that
  fires 10 times a day uses about 300 runs a month.
- **Workspaces** — separate isolation boundaries, typically one per team or project.
- **Members / workspace** — distinct people with access. Billing counts the peak in a
  period, not the average.
- **Storage** — total bytes of run history, snapshots, and artifacts, measured at
  month-end.
- **Run retention** — how long completed run history is kept before it's archived out
  of the live view.
- **API rate limit** — how many API calls per minute external systems can make.
- **AI models** — Community uses Anthropic (Claude) with your own key. Starter adds
  Azure OpenAI and Google. Pro unlocks every provider. Enterprise adds bring-your-own
  model endpoints.

## When to upgrade

There's no artificial urgency — upgrade only when a limit actually constrains you:

- **If you hit 500 runs in a month**, upgrade to Starter (5,000 runs).
- **If you need a second person in a workspace**, you need Cloud — Community is
  single-user.
- **If you regularly cross 5,000 runs or want unlimited workflows**, move to Pro.
- **If you need SSO, data residency, or customer-managed encryption keys**, look at
  [Enterprise self-hosted](../billing/enterprise-self-hosted).

Upgrades take effect immediately and are prorated. See the
[Upgrade guide](../billing/upgrade-guide).

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/getting-started/plans-comparison.md)
