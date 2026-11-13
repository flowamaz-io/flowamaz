<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ChevronDown, ChevronUp, ArrowLeft } from 'lucide-vue-next';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import CredentialSetupWizard from '@/components/library/CredentialSetupWizard.vue';
import RateConnectorModal from '@/components/library/RateConnectorModal.vue';
import { connectorService, formatInstalls } from '@/services/connector.service';
import { libraryService, noticeDate, type LibraryConnectorDetail, type LibraryOutdatedItem } from '@/services/library.service';
import { toUserFacingError } from '@/utils/error.util';
import { useWorkspace } from '@/composables/useWorkspace';
import { CONNECTOR_INSTALL_ROLE_NEEDED, canDesign } from '@/utils/constants';
import { fromNow } from '@/utils/date.util';
import type { ConnectorDefinition, ConnectorReview } from '@/services/connector.service';

interface OperationField {
  description?: string;
  type?: string;
}

interface Operation {
  id?: string;
  name?: string;
  description?: string;
  input_schema?: { properties?: Record<string, OperationField> };
}

interface ConnectorManifest {
  description?: string;
  auth?: { type?: string; description?: string };
  operations?: Operation[];
}

const route = useRoute();
const router = useRouter();
const ws = useWorkspace();
const { currentWorkspaceId, loadWorkspaces } = ws;

const connectorId = route.params.connectorId as string;
const connector = ref<ConnectorDefinition | null>(null);
const loading = ref(false);
const error = ref<string | null>(null);
const wizardOpen = ref(false);
const expandedOps = ref<Set<number>>(new Set());
const reviews = ref<ConnectorReview[]>([]);
const rateOpen = ref(false);
// This workspace's view: installed version/pin, changelog, a newer major, deprecation.
const library = ref<LibraryConnectorDetail | null>(null);
const outdated = ref<LibraryOutdatedItem | null>(null);
const upgrading = ref(false);
const upgradeError = ref<string | null>(null);
const upgradeDone = ref<string | null>(null);
const isInstalled = computed(() => library.value?.connector.installed ?? connector.value?.isInstalled ?? false);
// Upgrading to a new major is a workspace admin's call (FUNCTIONAL.md §23.8); the server checks too.
const canInstall = computed(() => canDesign(ws.current.value?.role));
const isAdmin = computed(() => {
  const role = ws.current.value?.role;
  return !role || role === 'Admin';
});

const manifest = computed<ConnectorManifest>(() => {
  if (!connector.value) return {};
  try {
    return JSON.parse(connector.value.manifestJson) as ConnectorManifest;
  } catch {
    return {};
  }
});

const operations = computed<Operation[]>(() => manifest.value.operations ?? []);

const tierClasses = computed<string>(() => {
  if (!connector.value) return '';
  const map: Record<string, string> = {
    Official: 'bg-blue-100 text-blue-700',
    Community: 'bg-green-100 text-green-700',
    Verified: 'bg-purple-100 text-purple-700',
    Marketplace: 'bg-amber-100 text-amber-700',
  };
  return map[connector.value.tier] ?? 'bg-gray-100 text-gray-600';
});

const categoryEmoji: Record<string, string> = {
  finance: '💰',
  messaging: '💬',
  database: '🗄️',
  devops: '🔧',
  productivity: '📅',
  hr: '👥',
  engineering: '⚙️',
  ai: '🤖',
};

const icon = computed<string>(() =>
  connector.value
    ? (categoryEmoji[connector.value.category.toLowerCase()] ?? '🔌')
    : '🔌',
);

async function load(): Promise<void> {
  if (!currentWorkspaceId.value) await loadWorkspaces();
  if (!currentWorkspaceId.value) return;

  loading.value = true;
  error.value = null;
  try {
    connector.value = await connectorService.getConnectorById(
      currentWorkspaceId.value,
      connectorId,
    );
    reviews.value = await connectorService.getReviews(currentWorkspaceId.value, connectorId);
    await loadLibrary();
  } catch (err: unknown) {
    error.value =
      err instanceof Error
        ? err.message
        : 'Failed to load connector details. Try again or contact support.';
  } finally {
    loading.value = false;
  }
}

async function loadLibrary(): Promise<void> {
  if (!currentWorkspaceId.value) return;
  const [detail, outdatedList] = await Promise.all([
    libraryService.getConnector(currentWorkspaceId.value, connectorId),
    libraryService.outdated(currentWorkspaceId.value),
  ]);
  library.value = detail;
  outdated.value = outdatedList.find((o) => o.id === connectorId && o.publisher === detail.connector.publisher) ?? null;
}

async function upgrade(): Promise<void> {
  if (!currentWorkspaceId.value || !outdated.value?.latestVersion) return;
  upgrading.value = true;
  upgradeError.value = null;
  try {
    const result = await libraryService.upgrade(currentWorkspaceId.value, connectorId);
    upgradeDone.value = `Upgraded from ${result.fromVersion} to ${result.toVersion} (${result.pin}). Check the workflows that use it in a test run.`;
    await loadLibrary();
  } catch (e) {
    upgradeError.value = toUserFacingError(e).message;
  } finally {
    upgrading.value = false;
  }
}

function toggleOp(idx: number): void {
  if (expandedOps.value.has(idx)) {
    expandedOps.value.delete(idx);
  } else {
    expandedOps.value.add(idx);
  }
}

function onInstalled(_credentialId: string): void {
  void loadLibrary();
  if (connector.value) {
    connector.value = { ...connector.value, isInstalled: true, installCount: connector.value.installCount + 1 };
  }
  wizardOpen.value = false;
}

async function onRated(result: { averageRating: number; ratingCount: number }): Promise<void> {
  if (connector.value) {
    connector.value = { ...connector.value, averageRating: result.averageRating, ratingCount: result.ratingCount };
  }
  if (currentWorkspaceId.value) {
    reviews.value = await connectorService.getReviews(currentWorkspaceId.value, connectorId);
  }
}

onMounted(load);
</script>

<template>
  <div class="space-y-6">
    <!-- Back -->
    <button
      class="flex items-center gap-1.5 text-sm text-slate-500 hover:text-slate-800 transition-colors"
      @click="router.back()"
    >
      <ArrowLeft class="h-4 w-4" />
      Back to Library
    </button>

    <!-- Loading -->
    <div
      v-if="loading"
      class="flex justify-center py-16"
    >
      <FmSpinner size="lg" />
    </div>

    <!-- Error -->
    <FmErrorState
      v-else-if="error"
      title="Couldn't load connector"
      :description="error"
      action-label="Try again"
      @action="load"
    />

    <!-- Content -->
    <template v-else-if="connector">
      <!-- Header card -->
      <div class="rounded-xl border border-slate-200 bg-white p-6">
        <div class="flex items-start gap-4">
          <span class="text-4xl leading-none">{{ icon }}</span>
          <div class="flex-1 space-y-1">
            <div class="flex flex-wrap items-center gap-2">
              <h1 class="text-xl font-semibold text-slate-900">
                {{ connector.displayName }}
              </h1>
              <span :class="['rounded-full px-2 py-0.5 text-xs font-medium', tierClasses]">
                {{ connector.tier }}
              </span>
              <span
                v-if="!connector.isOfficial"
                class="rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-700"
              >
                Community
              </span>
              <span
                class="text-xs text-slate-400"
                data-test="connector-detail-version"
              >v{{ library?.connector.installedVersion ?? library?.connector.version ?? connector.version }}</span>
              <span
                v-if="library?.connector.installedPin ?? library?.connector.pin"
                class="rounded bg-slate-100 px-1.5 py-0.5 font-mono text-xs text-slate-500"
                :title="isInstalled ? 'This workspace runs this major; minor and patch releases apply automatically.' : 'New installs get this major.'"
              >{{ library?.connector.installedPin ?? library?.connector.pin }}</span>
            </div>
            <p class="text-sm text-slate-500">
              by {{ connector.publisherId }}
            </p>
            <div class="mt-1 flex items-center gap-4 text-sm text-slate-500">
              <span>{{ formatInstalls(connector.installCount) }} installs</span>
              <span>
                ⭐ {{ connector.ratingCount > 0 ? `${connector.averageRating.toFixed(1)} (${connector.ratingCount})` : 'No ratings' }}
              </span>
            </div>
            <p class="mt-2 text-sm text-slate-700 leading-relaxed">
              {{ manifest.description ?? connector.displayName }}
            </p>
          </div>
          <p
            v-if="!isInstalled && !canInstall"
            class="max-w-xs text-right text-xs text-slate-500"
            data-testid="connector-install-role-note"
          >
            {{ CONNECTOR_INSTALL_ROLE_NEEDED }}
          </p>
          <button
            v-else-if="!isInstalled"
            class="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-700 transition-colors whitespace-nowrap"
            @click="wizardOpen = true"
          >
            Install &amp; Connect
          </button>
          <span
            v-else
            class="rounded-lg bg-gray-100 px-4 py-2 text-sm font-semibold text-gray-500 whitespace-nowrap"
          >
            ✓ Installed
          </span>
        </div>
      </div>

      <!-- Deprecation notice -->
      <div
        v-if="library?.connector.deprecatedAt"
        class="rounded-xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900"
        role="status"
        data-test="connector-deprecation"
      >
        <p class="font-medium">
          This connector is deprecated and leaves the Library on {{ noticeDate(library.connector.removalAt) }}.
        </p>
        <p class="mt-1">
          {{ library.connector.deprecationNote }}
          {{ isInstalled ? 'It keeps working here; move the workflows that use it to another connector before then.' : 'Choose another connector for new workflows.' }}
        </p>
      </div>

      <!-- New major available -->
      <div
        v-if="outdated?.latestVersion"
        class="rounded-xl border border-indigo-200 bg-indigo-50 p-5"
        data-test="connector-upgrade"
      >
        <h2 class="text-sm font-semibold text-indigo-900">
          Version {{ outdated.latestVersion }} is available
        </h2>
        <p class="mt-1 text-sm text-indigo-900">
          This workspace runs {{ outdated.installedVersion }} ({{ outdated.installedPin }}). A new major release can change how
          actions behave, so it isn't applied automatically. Review what changed, then upgrade and run your workflows in test mode.
        </p>
        <ul
          v-if="outdated.changes.length > 0"
          class="mt-3 space-y-2"
        >
          <li
            v-for="c in outdated.changes"
            :key="c.version"
            class="text-sm text-slate-700"
          >
            <span class="font-mono font-medium">{{ c.version }}</span>
            <span
              v-if="c.releasedOn"
              class="text-slate-500"
            > · {{ c.releasedOn }}</span>
            <p class="whitespace-pre-line">
              {{ c.notes || 'No release notes.' }}
            </p>
          </li>
        </ul>
        <p
          v-else
          class="mt-2 text-sm text-slate-600"
        >
          The publisher didn't add release notes. Check the connector's README before upgrading.
        </p>
        <p
          v-if="upgradeError"
          class="mt-3 text-sm text-red-700"
          role="alert"
        >
          {{ upgradeError }}
        </p>
        <button
          v-if="isAdmin"
          type="button"
          class="mt-4 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-700 disabled:opacity-50"
          :disabled="upgrading"
          @click="upgrade"
        >
          {{ upgrading ? 'Upgrading…' : `Upgrade to ${outdated.latestVersion}` }}
        </button>
        <p
          v-else
          class="mt-3 text-sm text-slate-600"
        >
          Upgrading is up to a workspace admin. Ask one to upgrade it from this page.
        </p>
      </div>
      <p
        v-if="upgradeDone"
        class="rounded-xl border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-800"
        role="status"
      >
        {{ upgradeDone }}
      </p>

      <!-- Auth section -->
      <div class="rounded-xl border border-slate-200 bg-white p-5">
        <h2 class="mb-3 text-sm font-semibold text-slate-900 uppercase tracking-wide">
          Authentication
        </h2>
        <p class="text-sm text-slate-600">
          <span class="font-medium capitalize">{{ manifest.auth?.type ?? 'API Key' }}</span>
          <span
            v-if="manifest.auth?.description"
            class="ml-2 text-slate-500"
          >— {{ manifest.auth.description }}</span>
        </p>
      </div>

      <!-- Operations -->
      <div class="rounded-xl border border-slate-200 bg-white">
        <div class="border-b border-slate-200 px-5 py-4">
          <h2 class="text-sm font-semibold text-slate-900 uppercase tracking-wide">
            Operations ({{ operations.length }})
          </h2>
        </div>

        <div
          v-if="operations.length === 0"
          class="px-5 py-6 text-sm text-slate-400 text-center"
        >
          No operations defined in manifest.
        </div>

        <div
          v-for="(op, idx) in operations"
          :key="idx"
          class="border-b border-slate-100 last:border-0"
        >
          <button
            class="flex w-full items-center justify-between px-5 py-3 text-left hover:bg-slate-50 transition-colors"
            @click="toggleOp(idx)"
          >
            <div>
              <span class="text-sm font-medium text-slate-800">
                {{ op.name ?? op.id ?? `Operation ${idx + 1}` }}
              </span>
              <span
                v-if="op.description"
                class="ml-2 text-xs text-slate-400"
              >{{ op.description }}</span>
            </div>
            <component
              :is="expandedOps.has(idx) ? ChevronUp : ChevronDown"
              class="h-4 w-4 text-slate-400 shrink-0"
            />
          </button>

          <div
            v-if="expandedOps.has(idx) && op.input_schema?.properties"
            class="border-t border-slate-100 bg-slate-50 px-5 py-3"
          >
            <p class="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">
              Input fields
            </p>
            <table class="w-full text-xs">
              <thead>
                <tr class="text-left text-slate-500">
                  <th class="pb-1 pr-4 font-medium">
                    Field
                  </th>
                  <th class="pb-1 pr-4 font-medium">
                    Type
                  </th>
                  <th class="pb-1 font-medium">
                    Description
                  </th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="(field, name) in op.input_schema.properties"
                  :key="name"
                  class="border-t border-slate-200"
                >
                  <td class="py-1 pr-4 font-mono text-slate-700">
                    {{ name }}
                  </td>
                  <td class="py-1 pr-4 text-slate-500">
                    {{ field.type ?? 'string' }}
                  </td>
                  <td class="py-1 text-slate-500">
                    {{ field.description ?? '—' }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
      <!-- Changelog -->
      <div
        v-if="library && library.changelog.length > 0"
        class="rounded-xl border border-slate-200 bg-white p-5"
      >
        <h2 class="mb-3 text-sm font-semibold uppercase tracking-wide text-slate-900">
          Release notes
        </h2>
        <ul class="space-y-3">
          <li
            v-for="c in library.changelog"
            :key="c.version"
            class="text-sm text-slate-700"
          >
            <span class="font-mono font-medium">{{ c.version }}</span>
            <span
              v-if="c.releasedOn"
              class="text-slate-500"
            > · {{ c.releasedOn }}</span>
            <p class="whitespace-pre-line text-slate-600">
              {{ c.notes || 'No release notes.' }}
            </p>
          </li>
        </ul>
      </div>

      <!-- Reviews -->
      <div class="rounded-xl border border-slate-200 bg-white">
        <div class="flex items-center justify-between border-b border-slate-200 px-5 py-4">
          <h2 class="text-sm font-semibold text-slate-900 uppercase tracking-wide">
            Reviews ({{ connector.ratingCount }})
          </h2>
          <button
            v-if="isInstalled"
            class="rounded-lg bg-indigo-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-indigo-700 transition-colors"
            @click="rateOpen = true"
          >
            Leave a review
          </button>
        </div>
        <div
          v-if="reviews.length === 0"
          class="px-5 py-6 text-center text-sm text-slate-400"
        >
          No reviews yet.
          <template v-if="isInstalled">
            Be the first to review this connector.
          </template>
          <template v-else>
            Install this connector to leave a review.
          </template>
        </div>
        <div
          v-for="(r, idx) in reviews"
          :key="idx"
          class="border-b border-slate-100 px-5 py-3 last:border-0"
        >
          <div class="flex items-center justify-between">
            <span class="text-sm text-amber-500">{{ '★'.repeat(r.rating) }}<span class="text-slate-200">{{ '★'.repeat(5 - r.rating) }}</span></span>
            <span class="text-xs text-slate-400">{{ fromNow(r.createdAt) }}</span>
          </div>
          <p
            v-if="r.review"
            class="mt-1 text-sm text-slate-700"
          >
            {{ r.review }}
          </p>
        </div>
      </div>
    </template>

    <!-- Rate modal -->
    <RateConnectorModal
      v-if="connector && currentWorkspaceId"
      v-model="rateOpen"
      :workspace-id="currentWorkspaceId"
      :connector-id="connectorId"
      @rated="onRated"
    />

    <!-- Wizard -->
    <CredentialSetupWizard
      v-if="connector && wizardOpen"
      :connector="connector"
      :model-value="wizardOpen"
      @update:model-value="wizardOpen = $event"
      @installed="onInstalled"
    />
  </div>
</template>
