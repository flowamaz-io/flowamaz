<script setup lang="ts">
import { onMounted } from 'vue';
import { usePlanLimits } from '@/composables/usePlanLimits';

const props = defineProps<{ collapsed?: boolean }>();

const { usage, isCommunity, workflowsUsed, workflowsLimit, load } = usePlanLimits();

onMounted(load);
</script>

<template>
  <!-- Only shown in the self-hosted Community edition. -->
  <div
    v-if="isCommunity && usage && !props.collapsed"
    class="mx-2 my-2 rounded-lg border border-sidebar-border bg-white/5 px-3 py-2"
  >
    <p class="text-xs font-semibold text-sidebar-text-primary">
      Community Edition
    </p>
    <p class="mt-0.5 text-xs text-sidebar-text-secondary">
      {{ workflowsUsed }}/{{ workflowsLimit }} workflows used
    </p>
    <a
      href="https://flowamaz.com/pricing"
      target="_blank"
      rel="noopener noreferrer"
      class="mt-1 inline-block text-xs font-medium text-sidebar-active hover:underline"
    >
      Upgrade →
    </a>
  </div>
</template>
