# Workflow Empathy

## What is Workflow Empathy?

Workflow Empathy analyses your workflow from the perspective of the people affected by it — the requesters, approvers, and stakeholders who interact with it day-to-day.

A technically correct workflow can still feel frustrating. Someone submits a request and hears nothing for three days. An approval happens silently and the requester only finds out by checking manually. Empathy analysis surfaces these gaps.

## The Empathy Score

The score (0–100) reflects how considerate your workflow is of the humans involved:

| Score | Meaning |
|-------|---------|
| 80–100 | Great experience — people are kept informed |
| 50–79 | Acceptable — some improvements recommended |
| 0–49 | Poor — requesters are likely confused or frustrated |

## Issue Types

### No Outcome Notification
The workflow never sends an email or message when it completes. The requester has no way to know the outcome without logging in to check.

**Fix:** Add an email or Slack notification node near the End node.

### Visibility Gap
The workflow has one or more human-gate wait periods, but sends no status updates to the requester during that time. The requester is left waiting with no information.

**Fix:** Add a notification action before and after each human gate.

### Long Wait
A human gate has a timeout longer than 72 hours. After three days without progress, requesters often assume their request was lost.

**Fix:** Add an automatic reminder notification after 24 hours of inactivity, or shorten the timeout.

### Multiple Emails
The workflow sends more than 3 emails, which can overwhelm the requester.

**Fix:** Consolidate status emails into fewer, more meaningful notifications.

## Reading the Panel

The right panel in Empathy mode shows:

- **Score** — the overall empathy score
- **Summary metrics** — emails sent, wait periods, status updates, and average days to outcome
- **Issues list** — each issue with a severity badge and an expandable "How to fix" section

An empty issues list means your workflow scores 100 — great work.
