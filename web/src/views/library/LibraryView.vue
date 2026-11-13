<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { Search } from 'lucide-vue-next';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import ConnectorCard from '@/components/library/ConnectorCard.vue';
import CredentialSetupWizard from '@/components/library/CredentialSetupWizard.vue';
import TemplateGalleryView from '@/views/library/TemplateGalleryView.vue';
import LibraryFormsPanel from '@/components/library/LibraryFormsPanel.vue';
import LibraryPacksPanel from '@/components/library/LibraryPacksPanel.vue';
import { connectorService } from '@/services/connector.service';
import { libraryService, type LibraryConnectorItem, type LibraryOutdatedItem } from '@/services/library.service';
import { useWorkspace } from '@/composables/useWorkspace';
import { canDesign } from '@/utils/constants';
import type { ConnectorDefinition } from '@/services/connector.service';

type LibTab = 'connectors' | 'templates' | 'forms' | 'packs';
type SortKey = 'installed' | 'newest' | 'az';

const CATEGORIES = ['all', 'finance', 'hr', 'engineering', 'database', 'messaging', 'devops', 'ai'] as const;
type Category = (typeof CATEGORIES)[number];

const route = useRoute();
const router = useRouter();
const { currentWorkspaceId, current, loadWorkspaces } = useWorkspace();
const canInstall = computed(() => canDesign(current.value?.role));

const activeTab = ref<LibTab>((route.query.tab as LibTab) ?? 'connectors');
const searchQuery = ref('');
const activeCategory = ref<Category>('all');
const sortKey = ref<SortKey>('installed');

const connectors = ref<ConnectorDefinition[]>([]);
// Per-workspace state (installed version, pin, deprecation, upgrades), keyed publisher/id.
const libraryItems = ref<Map<string, LibraryConnectorItem>>(new Map());
const outdatedItems = ref<Map<string, LibraryOutdatedItem>>(new Map());
const libKey = (publisher: string, id: string): string => `${publisher}/${id}`;
const libraryOf = (c: ConnectorDefinition): LibraryConnectorItem | undefined => libraryItems.value.get(libKey(c.publisherId, c.connectorId));
const outdatedOf = (c: ConnectorDefinition): LibraryOutdatedItem | undefined => outdatedItems.value.get(libKey(c.publisherId, c.connectorId));
const loading = ref(false);
const error = ref<string | null>(null);

const installingId = ref<string | null>(null);
const wizardOpen = ref(false);
const wizardConnector = ref<ConnectorDefinition | null>(null);

// Sync tab with query param.
watch(activeTab, (tab) => {
  void router.replace({ query: { tab } });
});

const filteredConnectors = computed<ConnectorDefinition[]>(() => {
  let list = connectors.value;

  if (activeCategory.value !== 'all') {
    list = list.filter((c) => c.category.toLowerCase() === activeCategory.value);
  }

  if (searchQuery.value.trim()) {
    const q = searchQuery.value.toLowerCase();
    list = list.filter(
      (c) =>
        c.displayName.toLowerCase().includes(q) ||
        c.category.toLowerCase().includes(q) ||
        c.tags.some((t) => t.toLowerCase().includes(q)),
    );
  }

  if (sortKey.value === 'az') {
    list = [...list].sort((a, b) => a.displayName.localeCompare(b.displayName));
  } else if (sortKey.value === 'newest') {
    list = [...list].reverse();
  }

  return list;
});

async function load(): Promise<void> {
  if (!currentWorkspaceId.value) await loadWorkspaces();
  if (!currentWorkspaceId.value) return;

  loading.value = true;
  error.value = null;
  try {
    const [all, items, outdated] = await Promise.all([
      connectorService.getAllConnectors(currentWorkspaceId.value),
      libraryService.listConnectors(currentWorkspaceId.value),
      libraryService.outdated(currentWorkspaceId.value),
    ]);
    libraryItems.value = new Map(items.map((i) => [libKey(i.publisher, i.id), i]));
    outdatedItems.value = new Map(outdated.map((i) => [libKey(i.publisher, i.id), i]));
    // A connector that left the Library (and isn't installed here) isn't offered.
    connectors.value = all.filter((c) => libraryItems.value.has(libKey(c.publisherId, c.connectorId)));
  } catch (err: unknown) {
    error.value =
      err instanceof Error
        ? err.message
        : 'Failed to load connectors. Check your network connection and try again.';
  } finally {
    loading.value = false;
  }
}

function openWizard(connectorId: string): void {
  const c = connectors.value.find((x) => x.connectorId === connectorId);
  if (!c) return;
  wizardConnector.value = c;
  wizardOpen.value = true;
  installingId.value = connectorId;
}

function onInstalled(_credentialId: string): void {
  if (installingId.value) {
    const idx = connectors.value.findIndex((c) => c.connectorId === installingId.value);
    if (idx !== -1) {
      connectors.value[idx] = { ...connectors.value[idx], isInstalled: true };
      const c = connectors.value[idx];
      const item = libraryOf(c);
      if (item) libraryItems.value.set(libKey(c.publisherId, c.connectorId), { ...item, installed: true, installedVersion: item.version, installedPin: item.pin });
    }
  }
  installingId.value = null;
  wizardOpen.value = false;
  wizardConnector.value = null;
}

function onWizardClose(val: boolean): void {
  wizardOpen.value = val;
  if (!val) {
    installingId.value = null;
    wizardConnector.value = null;
  }
}

onMounted(load);
</script>

<template>
  <div class="space-y-6 px-8 py-6">
    <!-- Header -->
    <div>
      <h1 class="text-xl font-semibold text-slate-900">
        Library
      </h1>
      <p class="text-sm text-slate-500">
        Connectors, templates, and packs for your workflows.
      </p>
    </div>

    <!-- Tabs -->
    <div
      class="flex gap-1 rounded-lg border border-slate-200 bg-slate-50 p-1 w-fit"
      role="tablist"
      aria-label="Library sections"
    >
      <button
        v-for="tab in (['connectors', 'templates', 'forms', 'packs'] as const)"
        :key="tab"
        type="button"
        role="tab"
        :aria-selected="activeTab === tab"
        :class="[
          'rounded-md px-4 py-1.5 text-sm font-medium transition-colors',
          activeTab === tab
            ? 'bg-white text-slate-900 shadow-sm'
            : 'text-slate-500 hover:text-slate-700',
        ]"
        @click="activeTab = tab"
      >
        {{ tab.charAt(0).toUpperCase() + tab.slice(1) }}
      </button>
    </div>

    <!-- Connectors tab -->
    <div
      v-if="activeTab === 'connectors'"
      class="space-y-4"
    >
      <!-- Search + sort row -->
      <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div class="relative max-w-xs w-full">
          <Search class="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            v-model="searchQuery"
            type="text"
            placeholder="Search connectors..."
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
            A-Z
          </option>
        </select>
      </div>

      <!-- Category filter pills -->
      <div class="flex flex-wrap gap-2">
        <button
          v-for="cat in CATEGORIES"
          :key="cat"
          :class="[
            'rounded-full px-3 py-1 text-xs font-medium transition-colors capitalize',
            activeCategory === cat
              ? 'bg-indigo-600 text-white'
              : 'bg-slate-100 text-slate-600 hover:bg-slate-200',
          ]"
          @click="activeCategory = cat"
        >
          {{ cat === 'all' ? 'All' : cat }}
        </button>
      </div>

      <!-- Loading -->
      <div
        v-if="loading"
        class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3"
      >
        <div
          v-for="n in 3"
          :key="n"
          class="h-44 animate-pulse rounded-xl border border-slate-200 bg-slate-100"
        />
      </div>

      <!-- Error -->
      <FmErrorState
        v-else-if="error"
        title="Failed to load connectors"
        :description="error"
        action-label="Try again"
        @action="load"
      />

      <!-- Empty -->
      <FmEmptyState
        v-else-if="filteredConnectors.length === 0"
        :title="searchQuery ? `No connectors found for '${searchQuery}'` : 'No connectors in this category'"
        :description="searchQuery ? 'Try a different search term or contribute a connector.' : 'Try selecting a different category.'"
      >
        <template #action>
          <a
            href="https://github.com/flowamaz-io/connectors"
            target="_blank"
            rel="noopener"
            class="text-sm font-medium text-indigo-600 hover:text-indigo-800"
          >
            Contribute one →
          </a>
        </template>
      </FmEmptyState>

      <!-- Grid -->
      <div
        v-else
        class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3"
      >
        <ConnectorCard
          v-for="c in filteredConnectors"
          :key="c.connectorId"
          :connector="c"
          :is-installed="libraryOf(c)?.installed ?? false"
          :library="libraryOf(c)"
          :outdated="outdatedOf(c)"
          :can-install="canInstall"
          @install="openWizard"
        />
      </div>
    </div>

    <!-- Templates tab -->
    <TemplateGalleryView v-else-if="activeTab === 'templates'" />
    <LibraryFormsPanel v-else-if="activeTab === 'forms'" />
    <LibraryPacksPanel v-else-if="activeTab === 'packs'" />

    <!-- Wizard -->
    <CredentialSetupWizard
      v-if="wizardConnector"
      :connector="wizardConnector"
      :model-value="wizardOpen"
      @update:model-value="onWizardClose"
      @installed="onInstalled"
    />
  </div>
</template>
