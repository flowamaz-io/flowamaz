<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { Archive, ArchiveRestore, Pencil, Play, Plus, Trash2, Workflow } from 'lucide-vue-next';
import CreateWorkflowDialog from './CreateWorkflowDialog.vue';
import FmBadge from './FmBadge.vue';
import FmButton from './FmButton.vue';
import FmEmptyState from './FmEmptyState.vue';
import FmErrorState from './FmErrorState.vue';
import FmModal from './FmModal.vue';
import FmSkeleton from './FmSkeleton.vue';
import StartRunDialog from './StartRunDialog.vue';
import { describeError } from '../api';
import { fromNow } from '../date';
import { useWorkbenchApi } from '../host';
import { workflowStatusVariant } from '../workflows';
import type { WorkflowDefinitionListItem, WorkflowDefinitionResponse } from '../types';

// The workflow list on the WorkbenchApi alone: search, Active / All / Archived, create, edit, start a run,
// archive / unarchive (published) and delete (drafts). Emits navigation instead of using a router.
const props = withDefaults(defineProps<{ canEdit?: boolean; canRun?: boolean }>(), { canEdit: true, canRun: true });
const emit = defineEmits<{
  open: [workflowId: string];
  edit: [workflowId: string];
  'run-started': [runId: string];
  created: [workflow: WorkflowDefinitionResponse];
}>();

const api = useWorkbenchApi();
const workflows = ref<WorkflowDefinitionListItem[]>([]);
const loading = ref(true);
const error = ref<{ message: string; code?: string; status?: number } | null>(null);
const actionError = ref<string | null>(null);
const search = ref('');
const filter = ref<'active' | 'all' | 'archived'>('active');
const busy = ref<string | null>(null);
const createOpen = ref(false);
const runTarget = ref<WorkflowDefinitionListItem | null>(null);
const deleteTarget = ref<WorkflowDefinitionListItem | null>(null);

const archivedCount = computed(() => workflows.value.filter((w) => w.status === 'Archived').length);
const shown = computed(() => workflows.value.filter((w) => {
  const matches = w.name.toLowerCase().includes(search.value.trim().toLowerCase());
  const status = filter.value === 'all' || (filter.value === 'archived' ? w.status === 'Archived' : w.status !== 'Archived');
  return matches && status;
}));

async function load(): Promise<void> {
  loading.value = true;
  error.value = null;
  try {
    workflows.value = (await api.listWorkflows(1, 100)).items;
  } catch (e) {
    error.value = describeError(e);
  } finally {
    loading.value = false;
  }
}

async function act(id: string, action: () => Promise<unknown>): Promise<void> {
  busy.value = id;
  actionError.value = null;
  try {
    await action();
    await load();
  } catch (e) {
    actionError.value = describeError(e).message;
  } finally {
    busy.value = null;
  }
}

const archive = (w: WorkflowDefinitionListItem) => act(w.id, () => api.archiveWorkflow(w.id));
const unarchive = (w: WorkflowDefinitionListItem) => act(w.id, () => api.unarchiveWorkflow(w.id));
async function confirmDelete(): Promise<void> {
  const target = deleteTarget.value;
  deleteTarget.value = null;
  if (target) await act(target.id, () => api.deleteWorkflow(target.id));
}

function onCreated(workflow: WorkflowDefinitionResponse): void {
  emit('created', workflow);
  emit('edit', workflow.id);
}

onMounted(load);
defineExpose({ reload: load });
</script>

<template>
  <div class="space-y-6">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <div class="flex flex-wrap items-center gap-3">
        <input
          v-model="search"
          type="search"
          aria-label="Search workflows"
          placeholder="Search workflows"
          class="w-64 rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
        >
        <select
          v-model="filter"
          aria-label="Status"
          class="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
        >
          <option value="active">
            Active
          </option>
          <option value="all">
            All
          </option>
          <option value="archived">
            Archived ({{ archivedCount }})
          </option>
        </select>
      </div>
      <FmButton
        v-if="props.canEdit"
        data-testid="new-workflow"
        @click="createOpen = true"
      >
        <template #icon-left>
          <Plus class="h-4 w-4" />
        </template>
        New workflow
      </FmButton>
    </div>

    <p
      v-if="actionError"
      role="alert"
      class="rounded-lg border border-danger-200 bg-danger-50 px-4 py-3 text-sm text-danger-700"
    >
      {{ actionError }}
    </p>

    <div
      v-if="loading"
      class="space-y-3"
    >
      <FmSkeleton
        :count="5"
        height="h-14"
        rounded="lg"
      />
    </div>
    <FmErrorState
      v-else-if="error"
      title="Couldn't load workflows"
      :description="error.message"
      :status-code="error.status"
      :error-code="error.code"
      action-label="Try again"
      @action="load"
    />
    <FmEmptyState
      v-else-if="workflows.length === 0"
      :icon="Workflow"
      title="No workflows yet"
      description="Create one from a name: it starts with a trigger, an approval and an end, ready to edit and publish."
      :cta-label="props.canEdit ? 'New workflow' : undefined"
      @cta="createOpen = true"
    />
    <FmEmptyState
      v-else-if="shown.length === 0"
      :icon="Workflow"
      title="No workflows match"
      :description="filter === 'active' && archivedCount > 0
        ? 'Nothing active matches. Archived workflows are hidden; choose All or Archived in the status filter to see them.'
        : 'Nothing matches this search. Clear the search box or change the status filter.'"
    />
    <div
      v-else
      class="overflow-hidden rounded-xl border border-slate-200 bg-white"
    >
      <table class="min-w-full divide-y divide-slate-200 text-sm">
        <thead class="bg-slate-50 text-left text-xs font-semibold uppercase text-slate-500">
          <tr>
            <th class="px-4 py-3">
              Workflow
            </th>
            <th class="px-4 py-3">
              Status
            </th>
            <th class="px-4 py-3">
              Updated
            </th>
            <th class="px-4 py-3" />
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100">
          <tr
            v-for="w in shown"
            :key="w.id"
            class="hover:bg-slate-50"
          >
            <td class="px-4 py-3">
              <button
                type="button"
                class="font-medium text-slate-800 hover:text-primary-700"
                @click="emit('open', w.id)"
              >
                {{ w.name }}
              </button>
              <div class="font-mono text-xs text-slate-400">
                {{ w.slug }}
              </div>
            </td>
            <td class="px-4 py-3">
              <FmBadge :variant="workflowStatusVariant(w.status)">
                {{ w.status }}
              </FmBadge>
            </td>
            <td class="px-4 py-3 text-slate-500">
              {{ fromNow(w.updatedAt) }}
            </td>
            <td class="px-4 py-3">
              <div class="flex justify-end gap-1">
                <FmButton
                  v-if="props.canRun && w.status !== 'Archived'"
                  variant="ghost"
                  size="sm"
                  :aria-label="`Start a run of ${w.name}`"
                  @click="runTarget = w"
                >
                  <Play class="h-4 w-4" />
                </FmButton>
                <FmButton
                  v-if="props.canEdit && w.status !== 'Archived'"
                  variant="ghost"
                  size="sm"
                  :aria-label="`Edit ${w.name}`"
                  @click="emit('edit', w.id)"
                >
                  <Pencil class="h-4 w-4" />
                </FmButton>
                <FmButton
                  v-if="props.canEdit && w.status === 'Published'"
                  variant="ghost"
                  size="sm"
                  :loading="busy === w.id"
                  :aria-label="`Archive ${w.name}`"
                  @click="archive(w)"
                >
                  <Archive class="h-4 w-4" />
                </FmButton>
                <FmButton
                  v-if="props.canEdit && w.status === 'Archived'"
                  variant="ghost"
                  size="sm"
                  :loading="busy === w.id"
                  :aria-label="`Unarchive ${w.name}`"
                  @click="unarchive(w)"
                >
                  <ArchiveRestore class="h-4 w-4" />
                </FmButton>
                <FmButton
                  v-if="props.canEdit && w.status === 'Draft'"
                  variant="ghost"
                  size="sm"
                  :loading="busy === w.id"
                  :aria-label="`Delete ${w.name}`"
                  @click="deleteTarget = w"
                >
                  <Trash2 class="h-4 w-4" />
                </FmButton>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <CreateWorkflowDialog
      v-model="createOpen"
      @created="onCreated"
    />
    <StartRunDialog
      :model-value="runTarget !== null"
      :workflow="runTarget"
      @update:model-value="(open: boolean) => { if (!open) runTarget = null; }"
      @started="(runId: string) => emit('run-started', runId)"
    />
    <FmModal
      :model-value="deleteTarget !== null"
      title="Delete this draft?"
      size="sm"
      @update:model-value="(open: boolean) => { if (!open) deleteTarget = null; }"
    >
      <p class="text-sm text-slate-600">
        “{{ deleteTarget?.name }}” will be deleted. It has never been published, so no production runs use it; any test runs of it go too.
      </p>
      <template #footer>
        <FmButton
          variant="ghost"
          @click="deleteTarget = null"
        >
          Keep it
        </FmButton>
        <FmButton
          variant="danger"
          data-testid="confirm-delete"
          @click="confirmDelete"
        >
          Delete
        </FmButton>
      </template>
    </FmModal>
  </div>
</template>
