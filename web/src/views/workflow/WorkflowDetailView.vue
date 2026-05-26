<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import { storeToRefs } from 'pinia';
import FmBadge from '@/components/common/FmBadge.vue';
import FmButton from '@/components/common/FmButton.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import { useWorkflowStore } from '@/stores/workflow.store';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { fromNow, formatDate } from '@/utils/date.util';
import { healthColorClass, instanceStatusVariant, workflowStatusVariant } from '@/utils/workflow.util';

type Tab = 'overview' | 'instances' | 'versions' | 'yaml';

const route = useRoute();
const store = useWorkflowStore();
const toast = useToast();
const { currentWorkflow, versions, instances, loading, error } = storeToRefs(store);

const id = computed(() => String(route.params.id));
const activeTab = ref<Tab>('overview');
const publishing = ref(false);

async function loadTab(tab: Tab): Promise<void> {
  activeTab.value = tab;
  if (tab === 'versions') await store.loadVersions(id.value);
  if (tab === 'instances') await store.loadInstances({ workflowDefinitionId: id.value });
}

async function publish(): Promise<void> {
  publishing.value = true;
  try {
    await store.publishWorkflow(id.value);
    toast.success('Workflow published.');
    if (activeTab.value === 'versions') await store.loadVersions(id.value);
  } catch (err) {
    toast.error(toUserFacingError(err).message);
  } finally {
    publishing.value = false;
  }
}

onMounted(() => store.loadWorkflow(id.value));
watch(id, () => store.loadWorkflow(id.value));
</script>

<template>
  <div class="space-y-6">
    <div
      v-if="loading && !currentWorkflow"
      class="flex justify-center py-12"
    >
      <FmSpinner size="lg" />
    </div>

    <FmErrorState
      v-else-if="error && !currentWorkflow"
      title="Couldn't load this workflow"
      :description="error"
      action-label="Back to workflows"
    />

    <template v-else-if="currentWorkflow">
      <div class="flex items-start justify-between">
        <div>
          <RouterLink
            to="/workflows"
            class="text-sm text-slate-400 hover:text-slate-600"
          >
            ← Workflows
          </RouterLink>
          <h1 class="mt-1 text-xl font-semibold text-slate-900">
            {{ currentWorkflow.name }}
          </h1>
          <div class="mt-2 flex items-center gap-3 text-sm">
            <FmBadge :variant="workflowStatusVariant(currentWorkflow.status)">
              {{ currentWorkflow.status }}
            </FmBadge>
            <span :class="['font-semibold', healthColorClass(currentWorkflow.healthScore)]">
              Health {{ currentWorkflow.healthScore }}
            </span>
            <span class="text-slate-400">{{ currentWorkflow.currentVersion }}</span>
          </div>
        </div>
        <FmButton
          :loading="publishing"
          @click="publish"
        >
          Publish
        </FmButton>
      </div>

      <div class="flex gap-1 border-b border-slate-200">
        <button
          v-for="tab in (['overview', 'instances', 'versions', 'yaml'] as Tab[])"
          :key="tab"
          type="button"
          :class="[
            '-mb-px border-b-2 px-4 py-2 text-sm font-medium capitalize',
            activeTab === tab ? 'border-primary-600 text-primary-700' : 'border-transparent text-slate-500 hover:text-slate-700',
          ]"
          @click="loadTab(tab)"
        >
          {{ tab }}
        </button>
      </div>

      <div v-if="activeTab === 'overview'">
        <dl class="grid grid-cols-2 gap-4 rounded-xl border border-slate-200 bg-white p-5 text-sm md:grid-cols-4">
          <div>
            <dt class="text-slate-400">
              Trigger type
            </dt>
            <dd class="font-medium text-slate-800">
              {{ currentWorkflow.triggerType }}
            </dd>
          </div>
          <div>
            <dt class="text-slate-400">
              Created via
            </dt>
            <dd class="font-medium text-slate-800">
              {{ currentWorkflow.createdByMethod }}
            </dd>
          </div>
          <div>
            <dt class="text-slate-400">
              Created
            </dt>
            <dd class="font-medium text-slate-800">
              {{ formatDate(currentWorkflow.createdAt) }}
            </dd>
          </div>
          <div>
            <dt class="text-slate-400">
              Updated
            </dt>
            <dd class="font-medium text-slate-800">
              {{ fromNow(currentWorkflow.updatedAt) }}
            </dd>
          </div>
          <div class="col-span-2 md:col-span-4">
            <dt class="text-slate-400">
              Description
            </dt>
            <dd class="text-slate-700">
              {{ currentWorkflow.nlDescription || currentWorkflow.description || 'No description.' }}
            </dd>
          </div>
        </dl>
      </div>

      <div
        v-else-if="activeTab === 'instances'"
        class="overflow-hidden rounded-xl border border-slate-200 bg-white"
      >
        <table class="min-w-full divide-y divide-slate-200 text-sm">
          <thead class="bg-slate-50 text-left text-xs font-semibold uppercase text-slate-500">
            <tr>
              <th class="px-4 py-3">
                Instance
              </th>
              <th class="px-4 py-3">
                Status
              </th>
              <th class="px-4 py-3">
                Started
              </th>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-100">
            <tr
              v-for="inst in instances"
              :key="inst.id"
              class="hover:bg-slate-50"
            >
              <td class="px-4 py-3">
                <RouterLink
                  :to="`/instances/${inst.id}`"
                  class="font-mono text-xs text-primary-600 hover:text-primary-700"
                >
                  {{ inst.id.slice(0, 8) }}
                </RouterLink>
              </td>
              <td class="px-4 py-3">
                <FmBadge :variant="instanceStatusVariant(inst.status)">
                  {{ inst.status }}
                </FmBadge>
              </td>
              <td class="px-4 py-3 text-slate-500">
                {{ fromNow(inst.startedAt ?? inst.createdAt) }}
              </td>
            </tr>
            <tr v-if="instances.length === 0">
              <td
                colspan="3"
                class="px-4 py-6 text-center text-sm text-slate-400"
              >
                No runs yet. Trigger one from the Workflows list.
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div
        v-else-if="activeTab === 'versions'"
        class="overflow-hidden rounded-xl border border-slate-200 bg-white"
      >
        <ul class="divide-y divide-slate-100 text-sm">
          <li
            v-for="v in versions"
            :key="v.id"
            class="flex items-center justify-between px-4 py-3"
          >
            <div>
              <span class="font-mono text-xs text-slate-700">{{ v.commitSha.slice(0, 12) }}</span>
              <span
                v-if="v.isProduction"
                class="ml-2"
              >
                <FmBadge variant="primary">production</FmBadge>
              </span>
              <p class="text-xs text-slate-400">{{ v.message }}</p>
            </div>
            <span class="text-slate-500">{{ formatDate(v.createdAt) }}</span>
          </li>
          <li
            v-if="versions.length === 0"
            class="px-4 py-6 text-center text-slate-400"
          >
            No versions yet. Publish to create the first production version.
          </li>
        </ul>
      </div>

      <div
        v-else
        class="overflow-hidden rounded-xl border border-slate-200 bg-slate-900"
      >
        <pre class="overflow-x-auto p-4 text-xs leading-relaxed text-slate-100"><code>{{ currentWorkflow.yamlContent }}</code></pre>
      </div>
    </template>
  </div>
</template>
