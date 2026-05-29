---
prompt-id: 05-06-sidebar-design-update
phase: 05
sequence: 6
roles: [Executor, Verifier, UI, UX]
type: feature
depends-on: [05-05-ci-cd-github-actions]
estimated-complexity: Medium
---

# App Shell Visual Update — Dark Sidebar + Final Navigation

## Context
The app shell built in Phase 1 uses a generic gray sidebar. Phase 5 is the right
time to apply the final visual design — dark sidebar with Flowamaz brand identity,
full navigation structure with all Phase 1-4 sections, and responsive behaviour.

## Objective
Update AppShell, Sidebar, and navigation to the final dark sidebar design.
Wire all navigation items including sections added in Phases 2-5.
Implement workspace switcher in sidebar header.

## Scope

### What to Build

**AppShell visual update:**

Sidebar background: `#0F1117` (near-black)
Active nav item: `border-l-2 border-teal-500` left border + `text-white` label + teal icon
Inactive nav items: `text-slate-400` icon and label, hover → `text-white bg-white/5`
Sidebar width: 220px expanded, 52px collapsed (icon-only)
Transition: smooth 200ms ease

**Sidebar header (top section):**
```
┌─────────────────────────────┐
│ ✦ Flowamaz          [≡]    │  ← logo + collapse toggle
│─────────────────────────────│
│ 🏢 Acme Corp               │  ← org name (small, slate-400)
│ 📁 Finance Team        ▾   │  ← workspace selector (clickable)
└─────────────────────────────┘
```

Workspace selector dropdown:
- Lists all user's workspaces across orgs
- Search input at top of dropdown
- Current workspace highlighted with teal dot
- "+ New Workspace" at bottom
- Closes on click outside or Escape

**Complete navigation structure:**

```
─── MAIN ───────────────────
🏠  Dashboard
⚡  Workflows
▶   Instances
✓   Gates         [badge: pending count, amber]
☁   Weather

─── BUILD ──────────────────
🎨  Library
⚙   Connectors    [sub: Health]

─── INSIGHTS ───────────────
📊  Analytics
🔬  Process Intel

─── DEVELOPER ──────────────  (shown to Designer+)
🔧  CLI Docs
📖  API Reference  [ext link icon]

─── BOTTOM ─────────────────
⚙   Settings
?   Help           [opens help panel]
👤  [user avatar]  [name + plan badge]
```

Section labels (MAIN, BUILD etc): `text-xs text-slate-600 uppercase tracking-wider px-3 py-2`
Not shown in collapsed mode.

**Collapsed mode (52px):**
- Section labels hidden
- Only icons shown, centred
- Tooltip on hover showing the nav item label
- Logo: just the ✦ sparkle icon

**Mobile (< 768px):**
- Sidebar hidden by default
- Hamburger in top-left (white on dark overlay)
- Tap hamburger → sidebar slides in from left as overlay
- Tap backdrop → closes sidebar
- No collapse/expand toggle on mobile — just show/hide

**Top bar removal:**
Phase 1 built a top bar with workspace selector. Remove it.
Workspace selector moves into sidebar header (as designed above).
Notifications bell + user avatar move to sidebar bottom.

**Bottom user section:**
```
┌─────────────────────────────┐
│ [TJ] Jagan Mohan           │  ← avatar initial + name
│      Starter Plan      ▾   │  ← plan badge + dropdown trigger
└─────────────────────────────┘
```
User dropdown: Profile, Billing, Logout.

**Tailwind config update:**
Add sidebar-specific colour tokens:
```js
sidebar: {
  bg: '#0F1117',
  border: '#1C1F26',
  active: '#1D9E75',
  hover: 'rgba(255,255,255,0.05)',
  text: {
    primary: '#FFFFFF',
    secondary: '#94A3B8',
    muted: '#475569',
  }
}
```

**Update all existing views:**
- Remove any reference to the old top bar height offset (pt-16 etc)
- Content area: `ml-[220px]` expanded, `ml-[52px]` collapsed, transition-all
- On mobile: no margin, full width

**Help articles update:**
Update `workspaces/what-is-a-workspace.md` with screenshot description
of the new sidebar (text description — no actual screenshot in article).

## Technical Requirements
- [ ] Zero style blocks — all Tailwind only
- [ ] Sidebar transition: 200ms ease, no layout jump
- [ ] Workspace selector: keyboard navigable (arrow keys, Enter, Escape)
- [ ] Collapsed mode: tooltips on all nav items
- [ ] Mobile: slide-in overlay with backdrop tap to close
- [ ] Gates badge: live count from /gates?status=pending
- [ ] Pending count badge: amber background, white text, rounded-full
- [ ] All navigation items from all phases wired to correct routes

## Acceptance Criteria
- [ ] Sidebar renders with #0F1117 background and teal active indicator
- [ ] Workspace selector opens dropdown with all workspaces
- [ ] Collapse toggle: sidebar animates from 220px to 52px
- [ ] Mobile: hamburger visible, sidebar slides in on tap
- [ ] Gates badge shows correct pending count
- [ ] All nav items navigate to correct routes
- [ ] No top bar remains — workspace selector in sidebar

## Output Expected
```
web/src/components/layout/AppShell.vue (updated)
web/src/components/layout/Sidebar.vue (rebuilt)
web/src/components/layout/WorkspaceSwitcher.vue (new)
web/src/components/layout/UserMenu.vue (moved from top bar)
web/tailwind.config.ts (sidebar tokens added)
web/src/assets/main.css (sidebar custom properties)
web/src/tests/Sidebar.test.ts
web/src/tests/WorkspaceSwitcher.test.ts
```
