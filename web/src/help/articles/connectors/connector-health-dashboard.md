---
title: Connector Health Dashboard
updated: 2026-05-26
readingTime: "3 min read"
plans: "All plans"
---

# Connector Health Dashboard

The **Library → Health** page shows the real-time status of every installed connector
credential. It refreshes automatically every 30 seconds.

## Status indicators

| Status | Meaning | Action |
|--------|---------|--------|
| 🟢 Healthy | Credential is valid and working | None needed |
| 🔴 Expired | OAuth token expired or API key revoked | Click **Reconnect** |
| 🟡 Rate Limited | Hitting the provider's API rate limit | Wait or request a higher quota from the provider |
| 🟠 Error | Authentication or network error | Check error message, then click **Reconnect** |

## Columns

- **Connector** — the connector ID (e.g. `slack`, `github`).
- **Credential** — the name you gave the credential when installing.
- **Status** — health state with optional error detail.
- **Last Used** — timestamp of the most recent successful API call.
- **Calls / Hour** — rolling count of calls in the last 60 minutes (useful for spotting
  rate-limit proximity).
- **Expires** — for OAuth credentials, when the token expires. Flowamaz refreshes tokens
  proactively, but if refresh fails this date shows when the token became invalid.

## Reconnecting an expired credential

1. Find the row with **Expired** or **Error** status.
2. Click **Reconnect**.
3. The Credential Setup Wizard opens. Complete the auth flow again.
4. The credential is updated and the status returns to **Healthy** within 30 seconds.

## Deleting a credential

To remove a credential, uninstall the connector from the Library. This removes all
credentials associated with that connector in this workspace.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/connectors/connector-health-dashboard.md)
