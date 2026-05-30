<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { Search } from 'lucide-vue-next';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import TemplateCard from '@/components/library/TemplateCard.vue';
import InstallTemplateModal from '@/components/library/InstallTemplateModal.vue';
import { templateService, type TemplateListItem } from '@/services/template.service';
import { useWorkspace } from '@/composables/useWorkspace';

const CATEGORIES = ['All', 'Finance', 'HR', 'IT', 'Legal', 'Operations', 'Custom'] as const;
type Category = (typeof CATEGORIES)[number];
type SortKey = 'installed' | 'newest' | 'az';

const router = useRouter();
const { currentWorkspaceId, loadWorkspaces } = useWorkspace();

const templates = ref<TemplateListItem[]>([]);
const loading = ref(false);
const error = ref<string | null>(null);

const searchQuery = ref('');
const activeCategory = ref<Category>('All');
const sortKey = ref<SortKey>('installed');

const installModalOpen = ref(false);
const selectedTemplate = ref<TemplateListItem | null>(null);
const installModal = ref<InstanceType<typeof InstallTemplateModal> | null>(null);

const visibleTemplates = computed<TemplateListItem[]>(() => {
  let list = templates.value;
  if (sortKey.value === 'az') {
    list = [...list].sort((a, b) => a.name.localeCompare(b.name));
  } else if (sortKey.value === 'newest') {
    list = [...list].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  } else {
    list = [...list].sort((a, b) => b.installCount - a.installCount);
  }
  return list;
});

async function load(): Promise<void> {
  loading.value = true;
  error.value = null;
  try {
    const page = await templateService.list({
      category: activeCategory.value === 'All' ? undefined : activeCategory.value,
      search: searchQuery.value.trim() || undefined,
      pageSize: 100,
    });
    templates.value = page.items;
  } catch (err: unknown) {
    error.value =
      err instanceof Error
        ? err.message
        : 'Failed to load templates. Check your network connection and try again.';
  } finally {
    loading.value = false;
  }
}

let searchTimer: ReturnType<typeof setTimeout> | undefined;
watch(searchQuery, () => {
  if (searchTimer) clearTimeout(searchTimer);
  searchTimer = setTimeout(() => void load(), 300);
});
watch(activeCategory, () => void load());

function openPreview(id: string): void {
  void router.push({ name: 'template-detail', params: { id } });
}

function openInstall(id: string): void {
  selectedTemplate.value = templates.value.find((t) => t.id === id) ?? null;
  if (selectedTemplate.value) installModalOpen.value = true;
}

async function confirmInstall(name: string): Promise<void> {
  if (!currentWorkspaceId.value) await loadWorkspaces();
  const workspaceId = currentWorkspaceId.value;
  const template = selectedTemplate.value;
  if (!workspaceId || !template) {
    installModal.value?.fail('No workspace is selected. Pick a workspace, then install the template.');
    return;
  }
  try {
    const workflowId = await templateService.install(workspaceId, template.id, name);
    installModalOpen.value = false;
    await router.push({
      name: 'workflow-editor',
      params: { id: workflowId },
      query: { workspaceId },
    });
  } catch (err: unknown) {
    installModal.value?.fail(
      err instanceof Error
        ? err.message
        : 'Could not install the template. Try again, or pick a different template.',
    );
  }
}

onMounted(load);
</script>

<template>
  <div class="space-y-4">
    <!-- Search + sort -->
    <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
      <div class="relative w-full max-w-xs">
        <Search class="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
        <input
          v-model="searchQuery"
          type="text"
          placeholder="Search templates..."
          class="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm text-slate-900 placeholder:text-slate-400 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200"
        >
      </div>
      <select
        v-model="sortKey"
        class="rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200"
      >
        <option value="installed">
          Most Installed
        </option>
        <option value="newest">
          Newest
        </option>
        <option value="az">
          Alphabetical
        </option>
      </select>
    </div>

    <!-- Category pills -->
    <div class="flex flex-wrap gap-2">
      <button
        v-for="cat in CATEGORIES"
        :key="cat"
        :class="[
          'rounded-full px-3 py-1 text-xs font-medium transition-colors',
          activeCategory === cat
            ? 'bg-indigo-600 text-white'
            : 'bg-slate-100 text-slate-600 hover:bg-slate-200',
        ]"
        @click="activeCategory = cat"
      >
        {{ cat }}
      </button>
    </div>

    <!-- Loading -->
    <div
      v-if="loading"
      class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3"
    >
      <div
        v-for="n in 6"
        :key="n"
        class="h-60 animate-pulse rounded-xl border border-slate-200 bg-slate-100"
      />
    </div>

    <!-- Error -->
    <FmErrorState
      v-else-if="error"
      title="Failed to load templates"
      :description="error"
      action-label="Try again"
      @action="load"
    />

    <!-- Empty -->
    <FmEmptyState
      v-else-if="visibleTemplates.length === 0"
      :title="searchQuery ? `No templates match '${searchQuery}'` : 'No templates in this category yet'"
      :description="
        searchQuery
          ? 'Try a different search term, or publish one of your own workflows as a template.'
          : 'Pick another category, or publish a workflow from its detail page to start the gallery.'
      "
      cta-label="Browse all"
      @cta="() => { searchQuery = ''; activeCategory = 'All'; }"
    />

    <!-- Grid -->
    <div
      v-else
      class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3"
    >
      <TemplateCard
        v-for="t in visibleTemplates"
        :key="t.id"
        :template="t"
        @preview="openPreview"
        @install="openInstall"
      />
    </div>

    <!-- Install modal -->
    <InstallTemplateModal
      ref="installModal"
      v-model="installModalOpen"
      :template="selectedTemplate"
      @confirm="confirmInstall"
    />
  </div>
</template>
