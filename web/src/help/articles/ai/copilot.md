---
title: Co-pilot
updated: 2026-05-30
readingTime: "3 min read"
plans: "All plans"
---

# Co-pilot

Co-pilot edits your workflow from plain-English commands. You describe the change you
want, Co-pilot returns a **YAML patch**, and you review it before it touches anything.

## How it works

Co-pilot pattern-matches common edits first. Most everyday requests — add a node,
rename, connect two steps, change a condition — match a known pattern and are handled
instantly with **zero AI cost**. Only the genuinely novel requests fall through to a
model call. In practice about 80% of edits never hit the AI.

## Giving commands

Open Co-pilot from the workflow editor and type what you want, for example:

- "Add a Human Gate after the trigger for manager approval."
- "Connect the AI node to the End node."
- "Change the condition on edge e2 to amount over 10000."
- "Insert a Wait node that pauses for one hour before the Action."

Be specific about which node or edge you mean — names and ids help Co-pilot target the
right place.

## The patch format

Co-pilot never edits silently. It returns a **patch** — a preview of exactly what will
change in the `flowamaz/v1` YAML, shown as a diff with additions and removals
highlighted. You then:

1. **Review** the diff against what you asked for.
2. **Apply** to accept it, or **Discard** to reject.
3. Edit further or run Co-pilot again.

Applying a patch saves a new draft commit, so it's tracked in version history and
fully reversible.

## Tips

- **One change at a time** gets cleaner, more accurate patches than a long list.
- **Name nodes clearly** so commands can reference them unambiguously.
- **Always read the diff** before applying — Co-pilot is fast, but you own the result.
- For big rewrites, consider regenerating from plain English instead of patching.

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/ai/copilot.md)
