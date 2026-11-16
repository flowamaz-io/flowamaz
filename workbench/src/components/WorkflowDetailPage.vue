<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
import FmBadge from './FmBadge.vue';
import FmButton from './FmButton.vue';
import FmEmptyState from './FmEmptyState.vue';
import FmErrorState from './FmErrorState.vue';
import FmSpinner from './FmSpinner.vue';
import RunTable from './RunTable.vue';
import StartRunDialog from './StartRunDialog.vue';
import { describeError } from '../api';
import { formatDate, fromNow } from '../date';
import { useWorkbenchApi } from '../host';
import { workflowStatusVariant } from '../workflows';
import type { InstanceListItem, WorkflowDefinitionResponse, WorkflowVersionResponse } from '../types';

// One workflow on the WorkbenchApi alone: status, Start run / Edit / Archive, its published versions and its runs.
const props = withDefaults(defineProps<{ workflowId: string; canEdit?: boolean; canRun?: boolean }>(), { canEdit: true, canRun: true });
const emit = defineEmits<{ edit: [workflowId: string]; 'open-run': [runId: string]; 'run-started': [runId: string]; back: [] }>();

const api = useWorkbenchApi();
const workflow = ref<WorkflowDefinitionResponse | null>(null);
const versions = ref<WorkflowVersionResponse[]>([]);
const runs = ref<InstanceListItem[]>([]);
const error = ref<{ message: string; code?: string; status?: number } | null>(null);
const actionError = ref<string | null>(null);
const busy = ref(false);
const tab = ref<'versions' | 'runs'>('versions');
const runOpen = ref(false);

async function load(): Promise<void> {
  error.value = null;
  try {
    const [w, v, r] = await Promise.all([
      api.getWorkflow(props.workflowId),
      api.listVersions(props.workflowId),
      api.listRuns({ workflowId: props.workflowId, includeTest: true, pageSize: 50 }),
    ]);
    workflow.value = w;
    versions.value = [...v].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
    runs.value = r.items;
  } catch (e) {
    error.value = describeError(e);
  }
}

async function setArchived(archived: boolean): Promise<void> {
  busy.value = true;
  actionError.value = null;
  try {
    workflow.value = archived ? await api.archiveWorkflow(props.workflowId) : await api.unarchiveWorkflow(props.workflowId);
  } catch (e) {
    actionError.value = describeError(e).message;
  } finally {
    busy.value = false;
  }
}

watch(() => props.workflowId, () => { workflow.value = null; void load(); });
onMounted(load);
</script>

<template>
  <div>
    <FmErrorState
      v-if="error && !workflow"
      :title="error.status === 404 ? 'Workflow not found' : 'Couldn\'t load this workflow'"
      :description="error.message"
      :status-code="error.status"
      :error-code="error.code"
      :action-label="error.status === 404 ? 'Back to workflows' : 'Try again'"
      @action="error.status === 404 ? emit('back') : load()"
    />
    <div
      v-else-if="!workflow"
      class="flex justify-center py-12"
    >
      <FmSpinner size="lg" />
    </div>
    <div
      v-else
      class="space-y-6"
    >
      <div class="flex items-start justify-between gap-4">
        <div>
          <button
            type="button"
            class="text-sm text-slate-400 hover:text-slate-600"
            @click="emit('back')"
          >
            ← Workflows
          </button>
          <h1 class="mt-1 text-lg font-semibold text-slate-900">
            {{ workflow.name }}
          </h1>
          <div class="mt-2 flex items-center gap-3 text-sm text-slate-500">
            <FmBadge :variant="workflowStatusVariant(workflow.status)">
              {{ workflow.status }}
            </FmBadge>
            <span class="font-mono text-xs">{{ workflow.slug }}</span>
            <span>Updated {{ fromNow(workflow.updatedAt) }}</span>
          </div>
          <p
            v-if="workflow.description"
            class="mt-2 max-w-2xl text-sm text-slate-600"
          >
            {{ workflow.description }}
          </p>
        </div>
        <div class="flex gap-2">
          <FmButton
            v-if="canRun && workflow.status !== 'Archived'"
            :variant="workflow.status === 'Draft' ? 'warning-outline' : 'primary'"
            data-testid="workflow-start-run"
            @click="runOpen = true"
          >
            {{ workflow.status === 'Draft' ? 'Test run' : 'Start run' }}
          </FmButton>
          <FmButton
            v-if="canEdit && workflow.status !== 'Archived'"
            variant="secondary"
            @click="emit('edit', workflow.id)"
          >
            Edit
          </FmButton>
          <FmButton
            v-if="canEdit && workflow.status === 'Published'"
            variant="secondary"
            :loading="busy"
            @click="setArchived(true)"
          >
            Archive
          </FmButton>
          <FmButton
            v-if="canEdit && workflow.status === 'Archived'"
            variant="secondary"
            :loading="busy"
            @click="setArchived(false)"
          >
            Unarchive
          </FmButton>
        </div>
      </div>

      <p
        v-if="actionError"
        role="alert"
        class="rounded-lg border border-danger-200 bg-danger-50 px-4 py-3 text-sm text-danger-700"
      >
        {{ actionError }}
      </p>

      <div class="flex gap-1 border-b border-slate-200">
        <button
          v-for="t in (['versions', 'runs'] as const)"
          :key="t"
          type="button"
          :class="[
            '-mb-px border-b-2 px-4 py-2 text-sm font-medium capitalize',
            tab === t ? 'border-primary-600 text-primary-700' : 'border-transparent text-slate-500 hover:text-slate-700',
          ]"
          @click="tab = t"
        >
          {{ t }}
        </button>
      </div>

      <template v-if="tab === 'versions'">
        <FmEmptyState
          v-if="versions.length === 0"
          title="Not published yet"
          description="Each publish makes a version that new runs use. Edit the workflow and choose Publish when it's ready."
          :cta-label="canEdit && workflow.status !== 'Archived' ? 'Edit workflow' : undefined"
          @cta="emit('edit', workflow.id)"
        />
        <ul
          v-else
          class="divide-y divide-slate-100 rounded-xl border border-slate-200 bg-white"
        >
          <li
            v-for="(v, i) in versions"
            :key="v.id"
            class="flex items-center justify-between px-4 py-3 text-sm"
          >
            <div>
              <span class="font-medium text-slate-800">{{ v.tagName ?? `Version ${versions.length - i}` }}</span>
              <span class="ml-2 font-mono text-xs text-slate-400">{{ v.commitSha.slice(0, 8) }}</span>
            </div>
            <div class="flex items-center gap-3 text-slate-500">
              <FmBadge
                v-if="v.isProduction"
                variant="primary"
              >
                In use
              </FmBadge>
              <span :title="formatDate(v.createdAt)">{{ fromNow(v.createdAt) }}</span>
            </div>
          </li>
        </ul>
      </template>

      <template v-else>
        <FmEmptyState
          v-if="runs.length === 0"
          title="No runs yet"
          description="Start a run here, or send the workflow's webhook, and its runs appear in this list."
          :cta-label="canRun && workflow.status !== 'Archived' ? 'Start run' : undefined"
          @cta="runOpen = true"
        />
        <RunTable
          v-else
          :runs="runs"
          :workflow-name="() => workflow?.name ?? ''"
          @open="(id: string) => emit('open-run', id)"
        />
      </template>

      <StartRunDialog
        v-model="runOpen"
        :workflow="workflow"
        @started="(runId: string) => { emit('run-started', runId); void load(); }"
      />
    </div>
  </div>
</template>
