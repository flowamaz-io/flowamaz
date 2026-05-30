<script setup lang="ts">
// Version history. Mirrors the git release tags (v0.5–v0.7); updated each release.
const releases = [
  {
    version: 'v0.7',
    date: 'May 2026',
    features: [
      'Stripe billing + self-service plan upgrades',
      'Append-only audit log with CSV export',
      'Template gallery: install and publish workflows',
      'Instance replay + step debugger',
    ],
  },
  {
    version: 'v0.6',
    date: 'April 2026',
    features: [
      'Connector & template marketplace',
      'Public REST API with workspace API keys',
      'SSO via SAML 2.0 and OIDC',
      'Community Edition (self-hosted)',
    ],
  },
  {
    version: 'v0.5',
    date: 'March 2026',
    features: [
      'Git-native workflow versioning',
      '@flowamaz/cli and VS Code extension',
      'Process intelligence + ROI analytics',
      'Workflow Weather health scores',
    ],
  },
];
</script>

<template>
  <div class="mx-auto max-w-3xl px-6 py-20">
    <h1 class="text-4xl font-bold text-ink">Changelog</h1>
    <p class="mt-3 text-lg text-slate-600">What we shipped, release by release.</p>

    <div class="mt-12 space-y-12">
      <section v-for="release in releases" :key="release.version" class="border-l-2 border-primary-200 pl-6">
        <div class="flex items-center gap-3">
          <span class="rounded-full bg-primary px-3 py-1 text-sm font-semibold text-white">{{ release.version }}</span>
          <span class="text-sm text-slate-400">{{ release.date }}</span>
        </div>
        <ul class="mt-4 space-y-2">
          <li v-for="feature in release.features" :key="feature" class="flex items-start gap-2 text-slate-600">
            <span class="mt-1 text-primary">✦</span>
            <span>{{ feature }}</span>
          </li>
        </ul>
      </section>
    </div>
  </div>
</template>
