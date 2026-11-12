<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { Dna, RefreshCw } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { inheritanceService, type InheritanceReport, type InheritanceSuggestion } from '@/services/inheritance.service';

// Workflow DNA inheritance (§8.8): patterns from similar workflows in this workspace that do better.

const props = defineProps<{ workspaceId: string; workflowId: string; canEdit: boolean }>();
const emit = defineEmits<{ applied: [] }>();
const toast = useToast();

const loading = ref(true);
const loadError = ref('');
const report = ref<InheritanceReport | null>(null);
const applying = ref<string | null>(null);

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = '';
  try {
    report.value = await inheritanceService.getReport(props.workspaceId, props.workflowId);
  } catch (e) {
    loadError.value = toUserFacingError(e).message;
  } finally {
    loading.value = false;
  }
}

onMounted(load);

const key = (s: InheritanceSuggestion) => `${s.kind}:${s.fromWorkflowId}`;

async function apply(s: InheritanceSuggestion): Promise<void> {
  applying.value = key(s);
  try {
    await inheritanceService.apply(props.workspaceId, props.workflowId, s.kind, s.fromWorkflowId);
    toast.success(`Added "${s.title.toLowerCase()}" from ${s.fromWorkflowName} and saved. Publish to use it in new runs.`);
    emit('applied');
    await load();
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    applying.value = null;
  }
}
</script>

<template>
  <section
    class="space-y-3 rounded-lg border border-gray-200 px-3 py-3"
    aria-labelledby="inheritance-heading"
    data-test="inheritance"
  >
    <div class="flex items-center justify-between">
      <h3
        id="inheritance-heading"
        class="flex items-center gap-1.5 text-xs font-medium tracking-wider text-gray-500 uppercase"
      >
        <Dna class="h-3.5 w-3.5" />
        Learn from similar workflows
      </h3>
      <button
        class="text-xs text-teal-600 hover:text-teal-700"
        aria-label="Refresh suggestions from similar workflows"
        @click="load"
      >
        <RefreshCw class="h-3.5 w-3.5" />
      </button>
    </div>

    <div
      v-if="loading"
      class="flex justify-center py-3 text-teal-600"
    >
      <FmSpinner />
    </div>

    <FmErrorState
      v-else-if="loadError"
      title="Couldn't load suggestions"
      :description="loadError"
      action-label="Try again"
      @action="load"
    />

    <template v-else-if="report">
      <p class="text-xs text-gray-600">
        Last 30 days: {{ report.performance.runs }} finished runs, {{ report.performance.successPercent }}% succeeded,
        SLA met {{ report.performance.slaCompliancePercent }}% of the time.
      </p>
      <p
        v-if="report.suggestions.length === 0"
        class="rounded-lg bg-gray-50 px-3 py-2 text-xs text-gray-600"
        data-test="inheritance-empty"
      >
        {{ report.message }}
      </p>
      <ul
        v-else
        class="space-y-2"
      >
        <li
          v-for="s in report.suggestions"
          :key="key(s)"
          class="space-y-1 rounded-lg border border-teal-200 bg-teal-50 px-3 py-2"
          data-test="inheritance-suggestion"
        >
          <div class="text-sm font-medium text-gray-900">
            {{ s.title }}
          </div>
          <p class="text-xs text-gray-700">
            {{ s.description }}
          </p>
          <p class="text-xs text-gray-500">
            From {{ s.fromWorkflowName }} ({{ s.similarity }}% similar). {{ s.evidence }}
          </p>
          <FmButton
            v-if="s.canApply && canEdit"
            size="sm"
            :loading="applying === key(s)"
            :disabled="applying !== null"
            @click="apply(s)"
          >
            Add to this workflow
          </FmButton>
          <p
            v-else-if="s.canApply"
            class="text-xs text-gray-500"
          >
            A Designer or Admin can add this here.
          </p>
          <p
            v-else
            class="text-xs text-gray-500"
          >
            Make this change on the canvas: it depends on who to tell and what to undo in this workflow.
          </p>
        </li>
      </ul>
    </template>
  </section>
</template>
