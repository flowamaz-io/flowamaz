---
title: Audit Log
updated: 2026-05-30
readingTime: "3 min read"
plans: "Cloud only"
---

# Audit Log

The audit log is an **append-only** record of significant actions in your workspace —
who did what, and when. It's there for security reviews, compliance, and figuring out
how something changed.

## What is logged

Each entry records the **actor** (user or API key), the **action**, the **target**,
and a **timestamp**. Logged actions include:

- Workflow create, update, publish, and rollback.
- Instance triggers, cancels, and replays.
- Human Gate decisions (who approved or rejected).
- Connector and credential changes.
- Member, role, and permission changes.
- API key creation and revocation.
- Billing and SSO configuration changes.

Entries are **append-only** — they can't be edited or deleted, so the record stays
trustworthy.

## Viewing and filtering

Open **Settings → Audit Log**. Filter by actor, action type, target, or date range to
narrow down to the event you're investigating — for example, "all credential changes
last month" or "everything this user did yesterday."

## CSV export

Export the filtered view to **CSV** for offline analysis, ticketing, or handing to
auditors. Apply your filters first; the export includes exactly the rows you see, with
the actor, action, target, and timestamp columns.

```text
timestamp,actor,action,target
2026-05-29T14:02:11Z,alex@acme.com,workflow.published,purchase-approval v3
2026-05-29T14:08:40Z,fmz_live_acme...,instance.triggered,purchase-approval
```

## Retention

Audit entries are kept according to your workspace's **retention policy**. Higher
plans retain history longer; export to CSV if you need to keep records beyond the
retention window. Once entries age past the policy, they're removed automatically —
exported copies are unaffected.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/settings/audit-log.md)
