<script setup lang="ts">
import { onMounted } from 'vue';
import { useRouter } from 'vue-router';
import OnboardingWizard from '@/components/onboarding/OnboardingWizard.vue';
import { useWorkspace } from '@/composables/useWorkspace';

const router = useRouter();
const ws = useWorkspace();

onMounted(() => {
  // Ensure the workspace list is loaded so creating one during onboarding stays in sync.
  ws.loadWorkspaces().catch(() => undefined);
});

function onFinished(): void {
  router.push('/');
}
</script>

<template>
  <div class="flex min-h-full items-center justify-center bg-slate-50 px-4 py-12">
    <OnboardingWizard @finished="onFinished" />
  </div>
</template>
