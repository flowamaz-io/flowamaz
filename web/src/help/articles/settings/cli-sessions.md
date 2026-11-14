---
title: CLI sessions
updated: 2026-11-14
readingTime: "2 min read"
plans: "All plans"
---

# CLI sessions

Each terminal where you run `fmz auth login` and approve the code in your browser gets a
**CLI session** of its own. **Settings → CLI sessions** lists yours: the name the terminal
gave itself (usually `fmz on <computer name>`), when it signed in, and when it was last used.

## Signing a terminal in

1. Run `fmz auth login`. The terminal shows a code and opens a page in your browser.
2. Check that the code on the page matches the terminal, then click **Approve sign-in**.

The terminal stays signed in while you use it: it quietly swaps its session token for a fresh
15-minute access token whenever it needs one. You don't have to sign in again every 15 minutes.

## When a session ends

- You run `fmz auth logout` in that terminal.
- You click **Sign out** next to it in **Settings → CLI sessions**. The terminal is cut off
  straight away, even in the middle of a command.
- Your password is reset, your account is deactivated, or your organisation starts requiring
  SSO (for members who aren't owners).
- It isn't used for 30 days.

After that, run `fmz auth login` again in the terminal.

## Good practice

- Sign out any session you don't recognise, then change your password.
- On a shared or CI machine, prefer an **API key** (`FMZ_API_KEY`) to a personal sign-in.
  Create one in **Settings → API Keys**.
- If `fmz auth logout` can't reach Flowamaz, it still forgets the sign-in on that machine and
  tells you. End the session here to be sure it can't be used again.
