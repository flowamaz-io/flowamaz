<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { marked } from 'marked';
import { Download } from 'lucide-vue-next';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmButton from '@/components/common/FmButton.vue';
import { instanceService } from '@/services/instance.service';
import { toUserFacingError } from '@/utils/error.util';
import type { InstanceStatus, NarrativeAudience } from '@/types';

const props = defineProps<{ workspaceId: string; instanceId: string; instanceStatus: InstanceStatus }>();

const isTerminal = computed(() =>
  props.instanceStatus === 'Completed' || props.instanceStatus === 'Failed' || props.instanceStatus === 'Cancelled',
);

const tabs = computed<{ id: NarrativeAudience; label: string; enabled: boolean }[]>(() => [
  { id: 'ceo', label: 'CEO', enabled: props.instanceStatus === 'Completed' },
  { id: 'auditor', label: 'Auditor', enabled: isTerminal.value },
  { id: 'developer', label: 'Developer', enabled: true },
]);

const active = ref<NarrativeAudience>('developer');
const content = ref('');
const loading = ref(false);
const error = ref<string | null>(null);

const renderedHtml = computed(() => marked.parse(content.value || '_No content._') as string);

async function load(audience: NarrativeAudience): Promise<void> {
  active.value = audience;
  loading.value = true;
  error.value = null;
  try {
    const narrative = await instanceService.narrative(props.workspaceId, props.instanceId, audience);
    content.value = narrative.content;
  } catch (err) {
    error.value = toUserFacingError(err).message;
    content.value = '';
  } finally {
    loading.value = false;
  }
}

async function download(): Promise<void> {
  const blob = await instanceService.downloadNarrative(props.workspaceId, props.instanceId, active.value);
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = `narrative-${props.instanceId}-${active.value}.md`;
  anchor.click();
  URL.revokeObjectURL(url);
}

onMounted(() => {
  const first = tabs.value.find((t) => t.enabled) ?? tabs.value[2]!;
  void load(first.id);
});
</script>

<template>
  <div class="rounded-xl border border-slate-200 bg-white">
    <div class="flex items-center justify-between border-b border-slate-200 px-4 py-3">
      <div class="flex gap-1">
        <button
          v-for="tab in tabs"
          :key="tab.id"
          type="button"
          :disabled="!tab.enabled"
          :class="[
            'rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
            active === tab.id ? 'bg-primary-50 text-primary-700' : 'text-slate-500 hover:bg-slate-100',
            !tab.enabled ? 'cursor-not-allowed opacity-40' : '',
          ]"
          @click="tab.enabled && load(tab.id)"
        >
          {{ tab.label }}
        </button>
      </div>
      <FmButton
        variant="secondary"
        size="sm"
        :disabled="loading || !content"
        @click="download"
      >
        <template #icon-left>
          <Download class="h-4 w-4" />
        </template>
        Download
      </FmButton>
    </div>

    <div class="px-4 py-4">
      <div
        v-if="loading"
        class="flex items-center gap-2 text-sm text-slate-500"
      >
        <FmSpinner size="sm" />
        Generating narrative…
      </div>
      <p
        v-else-if="error"
        class="text-sm text-danger-600"
      >
        {{ error }}
      </p>
      <!-- eslint-disable vue/no-v-html -- trusted: interpreter output is our own, sanitized server-side -->
      <div
        v-else
        class="prose prose-sm max-w-none text-slate-700"
        v-html="renderedHtml"
      />
      <!-- eslint-enable vue/no-v-html -->
    </div>
  </div>
</template>
