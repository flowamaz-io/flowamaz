<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import { CheckCircle2, Package, XCircle } from 'lucide-vue-next';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import FmButton from '@/components/common/FmButton.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import {
  libraryService, type LibraryPackInstallResult, type LibraryPackItem,
} from '@/services/library.service';
import { useWorkspace } from '@/composables/useWorkspace';
import { canDesign } from '@/utils/constants';

// Library → Packs (FUNCTIONAL.md §23.2): connectors, templates and forms for one domain, installed together.
const router = useRouter();
const { currentWorkspaceId, current, loadWorkspaces } = useWorkspace();
const canInstall = computed(() => canDesign(current.value?.role));

const packs = ref<LibraryPackItem[]>([]);
const loading = ref(false);
const error = ref<string | null>(null);
const installing = ref<string | null>(null);
const results = ref<Record<string, LibraryPackInstallResult>>({});
const installErrors = ref<Record<string, string>>({});

const KIND_LABEL = { connector: 'Connectors', template: 'Workflows', form: 'Forms' } as const;
const kinds = ['connector', 'template', 'form'] as const;

function entries(pack: LibraryPackItem, kind: (typeof kinds)[number]): LibraryPackItem['items'] {
  return pack.items.filter((i) => i.kind === kind);
}

function allInstalled(pack: LibraryPackItem): boolean {
  return pack.items.filter((i) => i.available).every((i) => i.installed);
}

async function load(): Promise<void> {
  if (!currentWorkspaceId.value) await loadWorkspaces();
  if (!currentWorkspaceId.value) return;
  loading.value = true;
  error.value = null;
  try {
    packs.value = await libraryService.listPacks(currentWorkspaceId.value);
  } catch (err: unknown) {
    error.value = err instanceof Error ? err.message : 'Packs could not be loaded. Check your connection, then select "Try again".';
  } finally {
    loading.value = false;
  }
}

async function install(pack: LibraryPackItem): Promise<void> {
  if (!currentWorkspaceId.value) return;
  installing.value = pack.slug;
  delete installErrors.value[pack.slug];
  try {
    results.value = { ...results.value, [pack.slug]: await libraryService.installPack(currentWorkspaceId.value, pack.slug) };
    packs.value = await libraryService.listPacks(currentWorkspaceId.value);
  } catch (err: unknown) {
    installErrors.value = {
      ...installErrors.value,
      [pack.slug]: err instanceof Error ? err.message : 'The pack was not installed. Check your connection and install again.',
    };
  } finally {
    installing.value = null;
  }
}

function summary(r: LibraryPackInstallResult): string {
  const parts: string[] = [];
  if (r.installed > 0) parts.push(`${r.installed} added`);
  if (r.alreadyInstalled > 0) parts.push(`${r.alreadyInstalled} already here`);
  if (r.failed > 0) parts.push(`${r.failed} couldn't be added — see below`);
  return parts.join(' · ');
}

function openWorkflow(id: string | null): void {
  if (id) void router.push({ name: 'workflow-detail', params: { id } }).catch(() => undefined);
}

onMounted(load);
</script>

<template>
  <div
    class="space-y-4"
    data-testid="library-packs"
  >
    <p class="text-sm text-slate-600">
      A pack sets up one area of work in one go: the connectors it needs, ready-made workflows and approval forms.
      Installing adds what's missing and leaves what you already have.
    </p>

    <div
      v-if="loading"
      class="flex justify-center py-12"
    >
      <FmSpinner size="lg" />
    </div>
    <FmErrorState
      v-else-if="error"
      title="Couldn't load packs"
      :description="error"
      action-label="Try again"
      @action="load"
    />
    <FmEmptyState
      v-else-if="packs.length === 0"
      :icon="Package"
      title="No packs in the Library yet"
      description="Packs appear here when they're published to the Library. Meanwhile, install connectors and templates one by one from their tabs."
      cta-label="Browse templates"
      @cta="router.replace({ query: { tab: 'templates' } })"
    />
    <div
      v-else
      class="grid gap-4 lg:grid-cols-2"
    >
      <article
        v-for="pack in packs"
        :key="pack.id"
        class="flex flex-col rounded-xl border border-slate-200 bg-white p-5"
        :data-testid="`library-pack-${pack.slug}`"
      >
        <div class="flex items-start justify-between gap-2">
          <div>
            <h3 class="font-semibold text-slate-900">
              {{ pack.name }}
            </h3>
            <p class="text-xs text-slate-500">
              {{ pack.category }} · v{{ pack.version }}
            </p>
          </div>
          <FmBadge :variant="pack.isOfficial ? 'primary' : 'slate'">
            {{ pack.isOfficial ? 'Official' : pack.publisher }}
          </FmBadge>
        </div>
        <p class="mt-2 text-sm text-slate-600">
          {{ pack.description }}
        </p>
        <p
          v-if="pack.deprecatedAt"
          class="mt-2 text-xs text-amber-700"
        >
          Deprecated: {{ pack.deprecationNote ?? 'its publisher no longer maintains it.' }}
        </p>

        <div class="mt-4 space-y-3">
          <template
            v-for="kind in kinds"
            :key="kind"
          >
            <div v-if="entries(pack, kind).length > 0">
              <p class="text-xs font-semibold uppercase tracking-wide text-slate-400">
                {{ KIND_LABEL[kind] }}
              </p>
              <ul class="mt-1 space-y-1">
                <li
                  v-for="item in entries(pack, kind)"
                  :key="item.id"
                  class="flex items-center justify-between text-sm"
                >
                  <span :class="item.available ? 'text-slate-800' : 'text-slate-400 line-through'">{{ item.name }}</span>
                  <span
                    v-if="!item.available"
                    class="text-xs text-slate-400"
                  >not in the Library</span>
                  <span
                    v-else-if="item.installed"
                    class="inline-flex items-center gap-1 text-xs text-emerald-700"
                  ><CheckCircle2 class="h-3.5 w-3.5" />{{ kind === 'form' ? 'ready' : 'in this workspace' }}</span>
                </li>
              </ul>
            </div>
          </template>
        </div>

        <div
          v-if="results[pack.slug]"
          class="mt-4 rounded-lg bg-slate-50 p-3 text-sm"
          role="status"
          :data-testid="`library-pack-result-${pack.slug}`"
        >
          <p class="font-medium text-slate-800">
            {{ summary(results[pack.slug]) }}
          </p>
          <ul class="mt-1 space-y-1 text-xs">
            <li
              v-for="r in results[pack.slug].items.filter((x) => x.status === 'failed' || x.status === 'unavailable' || x.workflowId)"
              :key="r.kind + r.id"
              class="flex items-start gap-1"
            >
              <XCircle
                v-if="r.status === 'failed' || r.status === 'unavailable'"
                class="mt-0.5 h-3.5 w-3.5 shrink-0 text-red-500"
              />
              <CheckCircle2
                v-else
                class="mt-0.5 h-3.5 w-3.5 shrink-0 text-emerald-600"
              />
              <span class="text-slate-600">
                {{ r.message }}
                <button
                  v-if="r.workflowId"
                  type="button"
                  class="ml-1 font-medium text-indigo-600 hover:text-indigo-800"
                  @click="openWorkflow(r.workflowId)"
                >Open</button>
              </span>
            </li>
          </ul>
        </div>
        <p
          v-if="installErrors[pack.slug]"
          class="mt-3 text-sm text-red-600"
          role="alert"
        >
          {{ installErrors[pack.slug] }}
        </p>

        <div class="mt-auto pt-4">
          <FmButton
            v-if="canInstall"
            :loading="installing === pack.slug"
            :disabled="allInstalled(pack)"
            :variant="allInstalled(pack) ? 'secondary' : 'primary'"
            :data-testid="`library-pack-install-${pack.slug}`"
            @click="install(pack)"
          >
            {{ allInstalled(pack) ? 'Everything is installed' : 'Install pack' }}
          </FmButton>
          <p
            v-else
            class="text-xs text-slate-500"
          >
            Installing a pack needs the Designer role. Ask a workspace admin to change your role in Settings → Members.
          </p>
        </div>
      </article>
    </div>
  </div>
</template>
