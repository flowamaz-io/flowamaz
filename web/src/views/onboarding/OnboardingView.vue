<script setup lang="ts">
import { onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import OnboardingWizard from '@/components/onboarding/OnboardingWizard.vue';
import { useWorkspace } from '@/composables/useWorkspace';
import { postLoginTarget } from '@/utils/redirect.util';

const router = useRouter();
const route = useRoute();
const ws = useWorkspace();

onMounted(() => {
  // Ensure the workspace list is loaded so creating one during onboarding stays in sync.
  ws.loadWorkspaces().catch(() => undefined);
});

function onFinished(): void {
  // Continue to the page the user originally asked for (validated same-origin path), else home.
  router.push(postLoginTarget(route.query.redirect, true));
}
</script>

<template>
  <div class="flex min-h-full items-center justify-center bg-slate-50 px-4 py-12">
    <OnboardingWizard @finished="onFinished" />
  </div>
</template>
