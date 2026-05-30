---
title: Single Sign-On (SSO)
updated: 2026-05-30
readingTime: "4 min read"
plans: "Enterprise"
---

# Single Sign-On (SSO)

SSO lets your team sign in to Flowamaz with your existing identity provider. Flowamaz
supports **SAML 2.0** and **OIDC**, both with just-in-time (JIT) provisioning. SSO is
available on Enterprise and Pro.

## SAML 2.0 setup

1. In your IdP (Okta, Entra ID, etc.), create a new SAML application.
2. In Flowamaz, open **Settings → SSO** and copy the **ACS URL** and **Entity ID**
   into your IdP's app config.
3. From your IdP, copy the **IdP metadata URL** (or upload the metadata XML) back into
   Flowamaz.
4. Map the IdP attributes Flowamaz expects: `email`, `firstName`, `lastName`.
5. Save, then use **Test connection** to verify a round-trip login before enabling.

## OIDC setup

1. In your IdP, register an OIDC application and note the **client ID**, **client
   secret**, and **issuer URL**.
2. In **Settings → SSO**, choose OIDC and paste those three values.
3. Add Flowamaz's **redirect URI** (shown on the SSO page) to your IdP's allowed
   callback list.
4. Save and run **Test connection**.

## JIT provisioning

With JIT provisioning on, you don't have to create accounts ahead of time. The first
time a user authenticates through your IdP, Flowamaz creates their account
automatically using the attributes from the SSO assertion.

- New users land in the org with a default role you configure on the SSO page.
- Existing users are matched by email and signed straight in.
- Deprovision users in your IdP — once they can't authenticate there, they can't reach
  Flowamaz.

## Enforcing SSO

Once SSO is verified, you can require it for your org so members must sign in through
your IdP rather than email and password. Keep at least one OrgOwner able to recover
access in case the IdP is unavailable.

## Troubleshooting

- **Login loops or assertion errors** — check the ACS URL / redirect URI and clock
  skew between systems.
- **Missing name fields** — confirm the attribute mappings match your IdP's claim
  names.
- **New users not created** — verify JIT provisioning is enabled and a default role is
  set.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/settings/sso.md)
