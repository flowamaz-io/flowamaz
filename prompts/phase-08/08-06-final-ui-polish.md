---
prompt-id: 08-06-final-ui-polish
phase: 08
sequence: 6
roles: [Executor, Verifier, UI, UX]
type: feature
depends-on: [08-05-launch-readiness]
estimated-complexity: Medium
---

# Final UI Polish — Consistency, Accessibility, Mobile

## Context
Testing session found several UI inconsistencies. This prompt addresses all
remaining visual and accessibility issues before the comprehensive testing session.

## Objective
Consistent visual language across all pages, WCAG AA accessibility compliance,
and working mobile layout.

## Scope

### What to Build

**Consistency audit — fix these specific issues:**

1. Content padding:
   Pages missing px-8 py-6: AuditLogView, BillingSettingsView, SsoSettingsView,
   WebhookSettingsView, WorkspaceOverviewView (if added in 08-04).
   Add px-8 py-6 to root container of each.

2. Page header pattern:
   Every page should follow the same pattern:
   ```
   <h1 class="text-2xl font-bold text-gray-900">Page Title</h1>
   <p class="text-sm text-gray-500 mt-1">Subtitle description</p>
   ```
   Audit all views and standardise the H1 + subtitle.

3. Empty states:
   All pages that can be empty must use FmEmptyState component.
   Check: NotificationsView (empty notifications), AuditLogView (no events yet),
   BillingSettingsView (no invoices), WebhookSettingsView (no webhooks).

4. Loading states:
   All data-fetching views must show FmSkeleton while loading.
   Check: AuditLogView, BillingSettingsView, WorkspaceOverviewView.

5. Error states:
   All views that call APIs must handle errors:
   Show FmAlert with the error message and a [Retry] button.
   Check: any view added in Phases 6-7 that may be missing error handling.

6. Button styles:
   Standardise button variants across all new views:
   Primary: bg-teal-500 text-white hover:bg-teal-600
   Secondary: border border-gray-300 text-gray-700 hover:bg-gray-50
   Danger: bg-red-500 text-white hover:bg-red-600
   Ghost: text-gray-600 hover:text-gray-900 hover:bg-gray-100

7. Form field styles:
   All form inputs should use the same classes:
   input: border border-gray-300 rounded-lg px-3 py-2 text-sm
          focus:outline-none focus:ring-2 focus:ring-teal-500 focus:border-teal-500
          placeholder:text-gray-400 bg-white text-gray-900

**Accessibility audit:**

1. All icon-only buttons must have aria-label:
   - Sidebar collapse toggle → aria-label="Toggle sidebar"
   - Close buttons (×) → aria-label="Close"
   - Copy buttons → aria-label="Copy to clipboard"

2. All modal dialogs must have:
   - role="dialog"
   - aria-labelledby pointing to the modal title
   - aria-modal="true"
   - Focus trap (Tab key stays within modal)
   - Escape key closes

3. All form inputs must have associated labels (not placeholder-only):
   Check all modals added in Phases 5-7.

4. Colour contrast:
   Verify teal (#1D9E75) on white passes WCAG AA (4.5:1 for normal text).
   Calculated: #1D9E75 on white = 3.8:1 — FAILS for normal text.
   Fix: use teal-600 (#0D7A59) for text on white backgrounds.
   Keep teal-500 for buttons (large text / UI components — 3:1 threshold).

5. Skip navigation link:
   Add to App.vue (hidden by default, visible on focus):
   ```html
   <a href="#main-content" 
     class="sr-only focus:not-sr-only focus:fixed focus:top-4 focus:left-4 
     focus:z-50 focus:bg-white focus:text-teal-600 focus:px-4 focus:py-2 
     focus:rounded-lg focus:shadow-lg">
     Skip to main content
   </a>
   ```

**Mobile layout audit:**

Test all major views at 375px width (iPhone SE):

1. Dashboard: creation method cards should stack to 2 columns on mobile
2. Workflows list: action buttons should be in a dropdown menu on mobile
3. Canvas: on mobile, canvas takes full screen, palette hidden (tap to show)
4. Library: connector cards should be single column on mobile
5. Settings: sidebar navigation collapses to a tab bar at bottom on mobile

Fix any layout breaks found. Use Tailwind responsive prefixes (sm:, md:).

**Dark mode preparation (not implementation — just tokens):**

Add CSS custom properties to main.css that will enable dark mode later:
```css
:root {
  --color-bg-primary: #FFFFFF;
  --color-bg-secondary: #F8FAFC;
  --color-text-primary: #111827;
  --color-text-secondary: #6B7280;
  --color-border: #E5E7EB;
}
/* Dark mode ready — implementation in future phase */
```

After all fixes:
npm run build --prefix web — 0 TypeScript errors, 0 lint errors
git add . && git commit -m "fix(ui): consistency audit, accessibility, mobile layout, contrast fix" && git push origin develop
