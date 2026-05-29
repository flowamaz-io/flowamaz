<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { storeToRefs } from 'pinia';
import { Pencil, Play, XCircle, AlertTriangle } from 'lucide-vue-next';
import TriggerModal from '@/components/workflow/TriggerModal.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import FmButton from '@/components/common/FmButton.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import FmYamlEditor from '@/components/editor/FmYamlEditor.vue';
import { useWorkflowStore } from '@/stores/workflow.store';
import { useWorkspaceStore } from '@/stores/workspace.store';
import { workflowService } from '@/services/workflow.service';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { fromNow, formatDate } from '@/utils/date.util';
import { healthColorClass, instanceStatusVariant, workflowStatusVariant } from '@/utils/workflow.util';

type Tab = 'overview' | 'instances' | 'versions' | 'yaml';

const route = useRoute();
const router = useRouter();
const store = useWorkflowStore();
const workspaceStore = useWorkspaceStore();
const toast = useToast();
const { currentWorkflow, versions, instances, loading, error } = storeToRefs(store);

const id = computed(() => String(route.params.id));
const workspaceId = computed(() => workspaceStore.currentWorkspaceId ?? '');
const activeTab = ref<Tab>('overview');
const publishing = ref(false);

const yamlContent = ref('');
const yamlDirty = ref(false);
const saving = ref(false);
const copied = ref(false);
const validating = ref(false);
const validationErrors = ref<Array<{ message: string; line?: number }>>([]);
const validationWarnings = ref<Array<{ message: string; line?: number }>>([]);

const triggerOpen = ref(false);
const forceTestRun = ref(false);

function openTriggerRun(): void {
  forceTestRun.value = false;
  triggerOpen.value = true;
}

function openTestRun(): void {
  forceTestRun.value = true;
  triggerOpen.value = true;
}

watch(currentWorkflow, (wf) => {
  if (wf && !yamlDirty.value) yamlContent.value = wf.yamlContent ?? '';
});

async function loadTab(tab: Tab): Promise<void> {
  activeTab.value = tab;
  if (tab === 'versions') await store.loadVersions(id.value);
  if (tab === 'instances') await store.loadInstances({ workflowDefinitionId: id.value });
  if (tab === 'yaml') yamlContent.value = currentWorkflow.value?.yamlContent ?? '';
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

async function saveYaml(): Promise<void> {
  saving.value = true;
  try {
    const updated = await workflowService.update(workspaceId.value, id.value, { yamlContent: yamlContent.value });
    currentWorkflow.value = updated;
    yamlDirty.value = false;
    store.bumpYamlVersion();
    toast.success('YAML saved.');
    await validateYaml(false);
  } catch (err) {
    toast.error(toUserFacingError(err).message);
  } finally {
    saving.value = false;
  }
}

async function copyYaml(): Promise<void> {
  try {
    await navigator.clipboard.writeText(yamlContent.value);
    copied.value = true;
    setTimeout(() => { copied.value = false; }, 2000);
  } catch {
    // clipboard unavailable
  }
}

function openInCanvas(): void {
  router.push({ name: 'workflow-editor', params: { id: id.value }, query: { workspace: workspaceId.value } });
}

async function validateYaml(showToast = true): Promise<void> {
  const content = yamlContent.value || currentWorkflow.value?.yamlContent;
  if (!content?.trim()) return;
  validating.value = true;
  validationErrors.value = [];
  validationWarnings.value = [];
  try {
    const result = await workflowService.validate(workspaceId.value, content);
    validationErrors.value = result.errors ?? [];
    validationWarnings.value = result.warnings ?? [];
    if (!showToast) return;
    const errorCount = result.errors.length;
    const warnCount = result.warnings.length;
    if (errorCount > 0) {
      toast.error(`Validation failed — ${errorCount} error${errorCount !== 1 ? 's' : ''} found. Fix before publishing.`);
    } else if (warnCount > 0) {
      toast.warning(`Valid with ${warnCount} warning${warnCount !== 1 ? 's' : ''} — can publish but review recommended`);
    } else {
      toast.success('Workflow is valid — ready to publish');
    }
  } catch {
    // silent — validation is best-effort
  } finally {
    validating.value = false;
  }
}

async function loadAndValidate(): Promise<void> {
  await store.loadWorkflow(id.value);
  await nextTick();
  if (currentWorkflow.value?.yamlContent) {
    yamlContent.value = currentWorkflow.value.yamlContent;
    await validateYaml(false);
  }
}

onMounted(loadAndValidate);
watch(id, () => store.loadWorkflow(id.value));
</script>

<template>
  <div class="px-8 py-6 space-y-6">
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
        <div class="flex items-center gap-3">
          <button
            class="flex items-center gap-2 px-4 py-2 border border-gray-300 rounded-lg text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors"
            @click="openInCanvas"
          >
            <Pencil class="w-4 h-4" />
            Edit in Canvas
          </button>
          <button
            class="flex items-center gap-2 px-4 py-2 border border-amber-400 rounded-lg text-sm font-medium text-amber-700 hover:bg-amber-50 transition-colors"
            @click="openTestRun"
          >
            <Play class="w-4 h-4" />
            Test run
          </button>
          <FmButton
            v-if="currentWorkflow.status !== 'Published'"
            :loading="publishing"
            @click="publish"
          >
            Publish
          </FmButton>
          <button
            v-if="currentWorkflow.status === 'Published'"
            class="flex items-center gap-2 px-4 py-2 bg-teal-500 text-white rounded-lg text-sm font-medium hover:bg-teal-600 transition-colors"
            @click="openTriggerRun"
          >
            <Play class="w-4 h-4" />
            Trigger run
          </button>
        </div>
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
          <span v-if="tab === 'yaml'" class="flex items-center gap-1.5">
            Yaml
            <span v-if="validationWarnings.length > 0 && validationErrors.length === 0"
              class="inline-flex items-center px-1.5 py-0.5 rounded text-xs font-medium bg-amber-100 text-amber-700">
              {{ validationWarnings.length }}
            </span>
            <span v-if="validationErrors.length > 0"
              class="inline-flex items-center px-1.5 py-0.5 rounded text-xs font-medium bg-red-100 text-red-700">
              {{ validationErrors.length }}
            </span>
          </span>
          <template v-else>{{ tab }}</template>
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

        <div class="mt-6 p-6 border border-gray-200 rounded-lg bg-gray-50 flex items-center justify-between">
          <div>
            <p class="font-medium text-gray-900">
              Ready to edit this workflow?
            </p>
            <p class="text-sm text-gray-500 mt-1">
              Open the visual canvas to drag nodes, connect steps, and build your flow.
            </p>
          </div>
          <button
            class="flex items-center gap-2 px-4 py-2 bg-teal-500 text-white rounded-lg text-sm font-medium hover:bg-teal-600 transition-colors"
            @click="openInCanvas"
          >
            <Pencil class="w-4 h-4" />
            Edit in Canvas
          </button>
        </div>
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
        class="border border-gray-200 rounded-lg overflow-hidden"
      >
        <div class="flex items-center justify-between px-4 py-2 bg-gray-50 border-b border-gray-200">
          <span class="text-xs font-medium text-gray-500 uppercase tracking-wider">YAML Editor</span>
          <div class="flex gap-2">
            <button
              class="text-xs text-gray-500 hover:text-gray-700 px-2 py-1 rounded hover:bg-gray-200 transition-colors"
              @click="copyYaml"
            >
              {{ copied ? 'Copied!' : 'Copy' }}
            </button>
            <button
              :disabled="validating"
              class="text-xs text-gray-600 border border-gray-300 px-3 py-1 rounded hover:bg-gray-100 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              @click="() => validateYaml(true)"
            >
              {{ validating ? 'Validating...' : 'Validate' }}
            </button>
            <button
              :disabled="!yamlDirty || saving"
              class="text-xs bg-teal-500 text-white px-3 py-1 rounded hover:bg-teal-600 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              @click="saveYaml"
            >
              {{ saving ? 'Saving...' : 'Save changes' }}
            </button>
          </div>
        </div>

        <div class="h-[600px]">
          <FmYamlEditor
            v-model="yamlContent"
            :dark-mode="false"
            @update:model-value="yamlDirty = true"
          />
        </div>

        <div
          v-if="validationErrors.length > 0 || validationWarnings.length > 0"
          class="border-t border-gray-200 bg-gray-50"
        >
          <div v-if="validationErrors.length > 0" class="p-3">
            <p class="text-xs font-semibold text-red-700 uppercase tracking-wider mb-2">
              Errors ({{ validationErrors.length }}) — must fix before publishing
            </p>
            <ul class="space-y-1">
              <li
                v-for="err in validationErrors"
                :key="err.message"
                class="flex items-start gap-2 text-sm text-red-700"
              >
                <XCircle class="w-4 h-4 flex-shrink-0 mt-0.5" />
                <span>{{ err.message }}</span>
              </li>
            </ul>
          </div>
          <div
            v-if="validationWarnings.length > 0"
            class="p-3"
            :class="validationErrors.length > 0 ? 'border-t border-gray-200' : ''"
          >
            <p class="text-xs font-semibold text-amber-700 uppercase tracking-wider mb-2">
              Warnings ({{ validationWarnings.length }}) — can publish but review recommended
            </p>
            <ul class="space-y-1">
              <li
                v-for="warn in validationWarnings"
                :key="warn.message"
                class="flex items-start gap-2 text-sm text-amber-700"
              >
                <AlertTriangle class="w-4 h-4 flex-shrink-0 mt-0.5" />
                <span>{{ warn.message }}</span>
              </li>
            </ul>
          </div>
        </div>
      </div>
    </template>

    <TriggerModal
      v-if="currentWorkflow"
      v-model="triggerOpen"
      :workflows="currentWorkflow ? [{ id: currentWorkflow.id, name: currentWorkflow.name, slug: currentWorkflow.slug, status: currentWorkflow.status, healthScore: currentWorkflow.healthScore, updatedAt: currentWorkflow.updatedAt }] : []"
      :preselected-id="currentWorkflow?.id"
      :force-test-run="forceTestRun"
    />
  </div>
</template>
