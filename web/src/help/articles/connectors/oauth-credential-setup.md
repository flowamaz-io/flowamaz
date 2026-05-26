---
title: OAuth Credential Setup
updated: 2026-05-26
readingTime: "4 min read"
plans: "All plans"
---

# OAuth Credential Setup

OAuth connectors (Slack, GitHub, Microsoft Teams, Microsoft 365, and others) use a
browser-based flow so Flowamaz never sees your password.

## How it works

1. Flowamaz requests an authorisation URL from the connector's OAuth provider.
2. A popup window opens on the provider's login page.
3. You grant permission. The provider redirects back to Flowamaz's callback URL.
4. Flowamaz stores the resulting access token as a **credential** — encrypted at rest,
   never visible after creation.

## Step by step

1. Open **Library**, find the OAuth connector, and click **Install & Connect**.
2. In the wizard, click **Connect with {Service}**.
3. Complete the OAuth flow in the popup window.
   - Log in if prompted.
   - Review the permissions Flowamaz is requesting and click **Authorise**.
4. The popup closes automatically when authorisation succeeds.
5. Give the credential a name and click **Done**.

## Timeouts and errors

| Symptom | Cause | Fix |
|---------|-------|-----|
| "OAuth timed out" | Popup open > 5 minutes | Close the popup and click Connect again |
| "Authorisation failed" | Permission denied in the popup | Try again and allow all requested permissions |
| Popup blocked | Browser popup blocker active | Allow popups for your Flowamaz domain in browser settings |

## Token refresh

Flowamaz automatically refreshes OAuth tokens before they expire. If a token cannot be
refreshed (e.g., you revoked access in the provider's settings), the credential status
changes to **Expired** in the Health dashboard. Click **Reconnect** to re-authorise.

## Revoking access

To remove Flowamaz's access from the provider's side:
1. Go to the provider's app authorisations or security settings.
2. Find Flowamaz and revoke access.
3. The credential in Flowamaz will show **Error** status — you can then uninstall the
   connector from the Library.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/connectors/oauth-credential-setup.md)
