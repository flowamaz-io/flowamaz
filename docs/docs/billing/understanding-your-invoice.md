---
title: Understanding Your Invoice
updated: 2026-05-24
readingTime: "3 min read"
plans: "Cloud only"
---

# Understanding Your Invoice

Your invoice shows your plan's base price plus any metered usage. Every line item is a
count — invoices never contain any workflow content.

## Line items explained

- **Workflow runs** — each time a workflow is triggered counts as one run. A run is
  counted at trigger time regardless of whether it succeeds, fails, or is cancelled.
  Retries of the same instance don't add new runs.
- **Members** — billed on the **peak** number of active members in the workspace during
  the period, not the average. Adding then removing someone mid-month still counts that
  peak.
- **Storage** — a single snapshot taken at **month-end** of how much run history,
  snapshots, and artifacts you're storing, in gigabytes.
- **AI calls** — only **platform-managed** AI calls are billed. If you bring your own key
  (BYOK), those calls go to your provider and are **not** charged by Flowamaz.

## Tracking your usage

Open **Settings → Usage** to see live counts for runs, members, storage, and AI calls
against your plan limits, updated through the period. Checking here mid-month tells you
whether you're trending toward a limit before the invoice arrives.

## Overage caps

Flowamaz protects you from runaway bills:

- **Organisation-level monthly cap** — we notify you at 50% and 80% of your run limit,
  and **block new triggers at 100%** rather than silently billing overages.
- **Workspace AI budget** — alerts at 80% and stops platform-managed AI calls at 100%.

Nothing runs over a cap without you choosing to raise it, so an invoice never surprises
you.

## Export as PDF

In **Settings → Billing**, open any invoice and click **Download PDF** for your records
or your finance team.

For how charges change when you move plans, see the [Upgrade guide](upgrade-guide).

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/billing/understanding-your-invoice.md)
