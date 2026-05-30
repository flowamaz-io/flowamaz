---
prompt-id: 08-02-marketing-site
phase: 08
sequence: 2
roles: [Executor, Verifier, UI, UX]
type: feature
depends-on: [08-01-deferred-fixes]
estimated-complexity: Medium
---

# Marketing Site — flowamaz.com Landing Page

## Context
flowamaz.com is the public marketing site. flowamaz.io is the technical/app domain.
The marketing site needs to convert visitors to signups and clearly communicate
what Flowamaz does for different buyer personas.

## Objective
Complete marketing site: landing page, pricing page, about page, and blog index.
Built as a separate Vue 3 SPA in the /marketing folder.

## Scope

### What to Build

**Project structure:**
```
marketing/
  src/
    views/
      HomeView.vue        ← main landing page
      PricingView.vue     ← pricing (mirrors app /pricing)
      AboutView.vue       ← company/mission
      BlogView.vue        ← blog index (static posts)
      ChangelogView.vue   ← product changelog
    components/
      NavBar.vue          ← top nav with CTA
      Footer.vue          ← links, social, legal
      HeroSection.vue     ← hero with headline + CTA
      FeatureSection.vue  ← feature highlights
      TestimonialSection.vue
      PricingCard.vue
    router/index.ts
    main.ts
  index.html
  vite.config.ts
  package.json
```

**HomeView.vue — Landing Page:**

NavBar:
- Logo (Flowamaz wordmark, teal ✦)
- Links: Product | Pricing | Blog | Docs (docs.flowamaz.io)
- CTA: [Start free trial] → app.flowamaz.io/register

Hero section:
- Headline: "Automate anything. In plain English."
- Sub: "Flowamaz turns your process descriptions into running workflows — with human approvals, AI decisions, and real-time monitoring built in."
- Primary CTA: [Start your free trial] → app.flowamaz.io/register
- Secondary CTA: [See how it works] → scrolls to demo section
- Hero visual: animated SVG workflow diagram (teal nodes connecting)

Social proof strip:
"Trusted by teams at [Company] [Company] [Company]" (placeholder logos)

Feature highlights (3 columns):
1. ✦ Plain English creation — "Describe your process. Flowamaz builds the workflow."
2. 👤 Human in the loop — "Approvals, reviews, and decisions built natively into every workflow."
3. 🔌 Connect everything — "13 official connectors. Any API via HTTP/REST."

How it works (3 steps with animated arrows):
1. Describe → 2. Review on canvas → 3. Run and monitor

Workflow Weather section:
"Know your process health at a glance" — screenshot of the weather dashboard

Pricing teaser:
Simple 3-column: Community (free) | Starter ($49/mo) | Pro ($149/mo)
[See full pricing] → /pricing

Footer:
Product: Features | Pricing | Changelog | Roadmap
Company: About | Blog | Careers | Press
Legal: Privacy Policy | Terms of Service | Security
Social: GitHub | Twitter/X | LinkedIn
© 2026 Flowamaz. All rights reserved.

**PricingView.vue:**
Same plan cards as the app /pricing page.
Monthly/annual toggle.
[Start free trial] on each plan links to app.flowamaz.io/register?plan={plan}

**AboutView.vue:**
Mission: "We're building the workflow automation platform that treats humans as first-class participants, not afterthoughts."
Team section (placeholder — "We're hiring")
Values: Transparency | Human-first | Developer-friendly

**BlogView.vue:**
Static blog posts (3 placeholder articles):
1. "Why we built Flowamaz" — founder story
2. "Human gates: the missing piece in workflow automation"
3. "From plain English to running workflow in 30 seconds"

**ChangelogView.vue:**
Version history extracted from git tags.
3 entries: v0.7 (Phase 7), v0.6 (Phase 6), v0.5 (Phase 5)
Each entry: date, version badge, bulleted features added.

**Design tokens:**
Same as app: #0F1117 dark, #1D9E75 teal, Inter font.
Marketing site feels lighter/more spacious than the app — more whitespace.
max-width: 1200px centered content.

**Build output:**
Built to marketing/dist/
Served separately from the app (different domain).

Unit tests (Vitest):
- NavBar: CTA links to correct app URL
- PricingCard: correct price displayed for monthly/annual
- HomeView: renders without errors

After build:
npm run build --prefix marketing — 0 errors
git add . && git commit -m "feat(marketing): flowamaz.com landing page, pricing, about, blog, changelog" && git push origin develop
