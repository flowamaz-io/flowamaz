<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { Workflow, Play } from 'lucide-vue-next';
import { storeToRefs } from 'pinia';
import FmBadge from '@/components/common/FmBadge.vue';
import FmButton from '@/components/common/FmButton.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import TriggerModal from '@/components/workflow/TriggerModal.vue';
import { useWorkflowStore } from '@/stores/workflow.store';
import { useWorkspace } from '@/composables/useWorkspace';
import { fromNow } from '@/utils/date.util';
import { CREATION_METHODS } from '@/utils/constants';
import { healthColorClass, workflowStatusVariant } from '@/utils/workflow.util';
import type { WorkflowStatus } from '@/types';

const store = useWorkflowStore();
const { workflows, loading, error } = storeToRefs(store);
const { currentWorkspaceId, loadWorkspaces } = useWorkspace();

const search = ref('');
const statusFilter = ref<WorkflowStatus | ''>('');
const triggerOpen = ref(false);
const preselectedId = ref<string | undefined>(undefined);

const filtered = computed(() =>
  workflows.value.filter((w) => {
    const matchesSearch = w.name.toLowerCase().includes(search.value.toLowerCase());
    const matchesStatus = statusFilter.value === '' || w.status === statusFilter.value;
    return matchesSearch && matchesStatus;
  }),
);

function openTrigger(id?: string): void {
  preselectedId.value = id;
  triggerOpen.value = true;
}

async function reload(): Promise<void> {
  if (!currentWorkspaceId.value) await loadWorkspaces();
  await store.loadWorkflows();
}

onMounted(reload);
</script>

<template>
  <div class="space-y-6">
    <div class="flex items-center justify-between">
      <div>
        <h1 class="text-xl font-semibold text-slate-900">
          Workflows
        </h1>
        <p class="text-sm text-slate-500">
          Design, publish and run your automations.
        </p>
      </div>
      <FmButton
        :disabled="workflows.length === 0"
        @click="openTrigger()"
      >
        <template #icon-left>
          <Play class="h-4 w-4" />
        </template>
        Trigger run
      </FmButton>
    </div>

    <div class="flex flex-wrap items-center gap-3">
      <input
        v-model="search"
        type="search"
        placeholder="Search by name…"
        class="w-64 rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
      >
      <select
        v-model="statusFilter"
        class="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
      >
        <option value="">
          All statuses
        </option>
        <option value="Draft">
          Draft
        </option>
        <option value="Published">
          Published
        </option>
        <option value="Archived">
          Archived
        </option>
      </select>
    </div>

    <div
      v-if="loading"
      class="flex justify-center py-12"
    >
      <FmSpinner size="lg" />
    </div>

    <FmErrorState
      v-else-if="error"
      title="Couldn't load workflows"
      :description="error"
      action-label="Try again"
      @action="reload"
    />

    <FmEmptyState
      v-else-if="workflows.length === 0"
      :icon="Workflow"
      title="Create your first workflow"
      description="Workflow creation methods arrive in Phase 3. Soon you'll build from plain English, a whiteboard photo, a conversation, a document, or the canvas."
    >
      <template #action>
        <div class="mt-5 flex flex-wrap justify-center gap-3">
          <div
            v-for="method in CREATION_METHODS"
            :key="method.id"
            class="flex flex-col items-center gap-1 text-slate-300"
          >
            <component
              :is="method.icon"
              class="h-6 w-6"
            />
            <span class="text-[10px]">{{ method.label }}</span>
          </div>
        </div>
      </template>
    </FmEmptyState>

    <div
      v-else
      class="overflow-hidden rounded-xl border border-slate-200 bg-white"
    >
      <table class="min-w-full divide-y divide-slate-200 text-sm">
        <thead class="bg-slate-50 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">
              Name
            </th>
            <th class="px-4 py-3">
              Status
            </th>
            <th class="px-4 py-3">
              Health
            </th>
            <th class="px-4 py-3">
              Updated
            </th>
            <th class="px-4 py-3 text-right">
              Actions
            </th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100">
          <tr
            v-for="wf in filtered"
            :key="wf.id"
            class="hover:bg-slate-50"
          >
            <td class="px-4 py-3">
              <RouterLink
                :to="`/workflows/${wf.id}`"
                class="font-medium text-slate-900 hover:text-primary-700"
              >
                {{ wf.name }}
              </RouterLink>
              <p class="text-xs text-slate-400">
                {{ wf.slug }}
              </p>
            </td>
            <td class="px-4 py-3">
              <FmBadge :variant="workflowStatusVariant(wf.status)">
                {{ wf.status }}
              </FmBadge>
            </td>
            <td class="px-4 py-3">
              <span :class="['font-semibold tabular-nums', healthColorClass(wf.healthScore)]">{{ wf.healthScore }}</span>
            </td>
            <td class="px-4 py-3 text-slate-500">
              {{ fromNow(wf.updatedAt) }}
            </td>
            <td class="px-4 py-3 text-right">
              <div class="flex justify-end gap-2">
                <RouterLink
                  :to="`/workflows/${wf.id}`"
                  class="text-sm font-medium text-primary-600 hover:text-primary-700"
                >
                  View
                </RouterLink>
                <button
                  type="button"
                  class="text-sm font-medium text-slate-600 hover:text-slate-900"
                  @click="openTrigger(wf.id)"
                >
                  Trigger
                </button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <TriggerModal
      v-model="triggerOpen"
      :workflows="workflows"
      :preselected-id="preselectedId"
    />
  </div>
</template>
