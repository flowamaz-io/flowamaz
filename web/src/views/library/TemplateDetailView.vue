<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ArrowLeft, Download } from 'lucide-vue-next';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import InstallTemplateModal from '@/components/library/InstallTemplateModal.vue';
import { templateService, type TemplateDetail } from '@/services/template.service';
import { useWorkspace } from '@/composables/useWorkspace';

const route = useRoute();
const router = useRouter();
const { currentWorkspaceId, loadWorkspaces } = useWorkspace();

const templateId = route.params.id as string;
const template = ref<TemplateDetail | null>(null);
const loading = ref(false);
const error = ref<string | null>(null);

const installModalOpen = ref(false);
const installModal = ref<InstanceType<typeof InstallTemplateModal> | null>(null);

const nodeSummaryLabel = computed<string>(() => {
  if (!template.value || template.value.nodeSummary.length === 0) return '';
  return template.value.nodeSummary.map((s) => `${s.count}× ${s.nodeType}`).join(', ');
});

async function load(): Promise<void> {
  loading.value = true;
  error.value = null;
  try {
    template.value = await templateService.get(templateId);
  } catch (err: unknown) {
    error.value =
      err instanceof Error
        ? err.message
        : 'Failed to load this template. It may have been removed — browse the gallery for current templates.';
  } finally {
    loading.value = false;
  }
}

async function confirmInstall(name: string): Promise<void> {
  if (!currentWorkspaceId.value) await loadWorkspaces();
  const workspaceId = currentWorkspaceId.value;
  if (!workspaceId || !template.value) {
    installModal.value?.fail('No workspace is selected. Pick a workspace, then install the template.');
    return;
  }
  try {
    const workflowId = await templateService.install(workspaceId, template.value.id, name);
    installModalOpen.value = false;
    await router.push({
      name: 'workflow-editor',
      params: { id: workflowId },
      query: { workspaceId },
    });
  } catch (err: unknown) {
    installModal.value?.fail(
      err instanceof Error ? err.message : 'Could not install the template. Try again.',
    );
  }
}

onMounted(load);
</script>

<template>
  <div class="space-y-6 px-8 py-6">
    <button
      type="button"
      class="flex items-center gap-1 text-sm text-slate-500 hover:text-slate-700"
      @click="router.push({ name: 'library', query: { tab: 'templates' } })"
    >
      <ArrowLeft class="h-4 w-4" />
      Back to templates
    </button>

    <div
      v-if="loading"
      class="flex justify-center py-16"
    >
      <FmSpinner />
    </div>

    <FmErrorState
      v-else-if="error"
      title="Couldn't load template"
      :description="error"
      action-label="Try again"
      @action="load"
    />

    <div
      v-else-if="template"
      class="grid gap-6 lg:grid-cols-3"
    >
      <!-- Left: details -->
      <div class="space-y-5 lg:col-span-2">
        <div class="flex items-start justify-between gap-3">
          <div>
            <h1 class="text-xl font-semibold text-slate-900">
              {{ template.name }}
            </h1>
            <div class="mt-1 flex items-center gap-2 text-sm text-slate-400">
              <span class="capitalize">{{ template.category }}</span>
              <span>·</span>
              <span>{{ template.installCount }} installs</span>
              <span>·</span>
              <span>v{{ template.version }}</span>
            </div>
          </div>
          <span
            :class="[
              'shrink-0 rounded-full px-2 py-0.5 text-xs font-medium',
              template.isOfficial ? 'bg-blue-100 text-blue-700' : 'bg-green-100 text-green-700',
            ]"
          >
            {{ template.isOfficial ? 'Official' : 'Community' }}
          </span>
        </div>

        <p class="text-sm leading-relaxed text-slate-600">
          {{ template.description }}
        </p>

        <div
          v-if="template.tags.length"
          class="flex flex-wrap gap-2"
        >
          <span
            v-for="tag in template.tags"
            :key="tag"
            class="rounded-full bg-slate-100 px-2 py-0.5 text-xs text-slate-600"
          >
            #{{ tag }}
          </span>
        </div>

        <div
          v-if="nodeSummaryLabel"
          class="rounded-lg border border-slate-200 bg-slate-50 px-4 py-3"
        >
          <p class="text-xs font-medium uppercase tracking-wide text-slate-400">
            This template uses
          </p>
          <p class="mt-1 text-sm text-slate-700">
            {{ nodeSummaryLabel }}
          </p>
        </div>

        <!-- YAML preview -->
        <div>
          <p class="mb-2 text-xs font-medium uppercase tracking-wide text-slate-400">
            Workflow definition
          </p>
          <pre class="max-h-96 overflow-auto rounded-lg border border-slate-200 bg-slate-900 p-4 text-xs leading-relaxed text-slate-100"><code>{{ template.yamlContent }}</code></pre>
        </div>
      </div>

      <!-- Right: preview + install -->
      <div class="space-y-4">
        <div class="flex h-40 items-center justify-center overflow-hidden rounded-xl border border-slate-200 bg-slate-50">
          <img
            v-if="template.previewImageUrl"
            :src="template.previewImageUrl"
            :alt="template.name"
            class="h-full w-full object-cover"
          >
          <span
            v-else
            class="text-5xl"
          >📋</span>
        </div>
        <button
          type="button"
          class="flex w-full items-center justify-center gap-2 rounded-lg bg-indigo-600 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-indigo-700"
          @click="installModalOpen = true"
        >
          <Download class="h-4 w-4" />
          Install template
        </button>
      </div>
    </div>

    <InstallTemplateModal
      ref="installModal"
      v-model="installModalOpen"
      :template="template"
      @confirm="confirmInstall"
    />
  </div>
</template>
