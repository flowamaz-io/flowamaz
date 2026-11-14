---
title: Workflow Twin
updated: 2026-11-14
readingTime: "3 min read"
plans: "All plans"
---

# Workflow Twin

The Workflow Twin finds the inputs that break a workflow before real users send them. It
simulates the workflow thousands of times, in memory, without running anything for real: no
runs are created, no connectors are called and no AI is used, so it costs nothing.

## What it tries

For every decision point (router, if/else, switch, and any edge with a condition) the Twin
reads the conditions and tries:

- **Boundary values**: each number a condition compares with, and the numbers just either
  side of it. `amount > 5000` gets 4999, 5000 and 5001 (and 4999.99 and 5000.01).
- **Other values**: a different letter case, a trailing space, a value that matches nothing.
- **Missing and badly formatted values**: the field left out, null or empty, `"abc"` where a
  number is compared, and `"5,000"` (a number written with a comma, as forms often send it).
- **Every approval both ways**: all approved, all rejected, and each one rejected on its own.
- **Recent real payloads**: the inputs of your last real runs. Steps that call connectors
  return what they returned in recent real runs.

One value is changed at a time, so each finding names exactly what breaks.

## What the results mean

- **Break**: a well-formed input (or a real payload, or a rejection) leaves the run with
  nowhere to go, or makes a router take two branches at once. For example, *"Breaks when
  amount = 5000"*: one branch says `< 5000` and the other `> 5000`, so exactly 5000 matches
  neither and the run stays Running forever.
- **Warning**: only a missing required field or a badly formatted value does it. Flowamaz
  doesn't reject such input today, so it's worth a default (else) branch.
- **Note**: a branch that no simulated input ever took. Its condition may read a field that
  nothing provides.

Each finding shows the values that matter and a suggested fix.

## When it runs

- **Published workflows** are simulated automatically once a day, and within an hour of each
  publish. The badge in the **Twin** column of the workflow list shows the latest result.
- **Anyone with Designer or Admin** can open a workflow's **Twin** tab and click
  **Run the Twin** at any time. An unpublished workflow is simulated from its saved draft.

## What it doesn't check

The Twin checks routing: whether every input finds its way to an end. It doesn't call your
connectors, so it can't tell whether a step's own settings (a URL, a credential) are right.
Use a test run for that.
