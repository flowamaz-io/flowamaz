<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ChevronDown, ChevronUp, ArrowLeft } from 'lucide-vue-next';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import CredentialSetupWizard from '@/components/library/CredentialSetupWizard.vue';
import { connectorService } from '@/services/connector.service';
import { useWorkspace } from '@/composables/useWorkspace';
import type { ConnectorDefinition } from '@/services/connector.service';

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
const { currentWorkspaceId, loadWorkspaces } = useWorkspace();

const connectorId = route.params.connectorId as string;
const connector = ref<ConnectorDefinition | null>(null);
const loading = ref(false);
const error = ref<string | null>(null);
const wizardOpen = ref(false);
const expandedOps = ref<Set<number>>(new Set());

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
  } catch (err: unknown) {
    error.value =
      err instanceof Error
        ? err.message
        : 'Failed to load connector details. Try again or contact support.';
  } finally {
    loading.value = false;
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
  if (connector.value) {
    connector.value = { ...connector.value, isInstalled: true };
  }
  wizardOpen.value = false;
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
              <span class="text-xs text-slate-400">v{{ connector.version }}</span>
            </div>
            <p class="text-sm text-slate-500">
              by {{ connector.publisherId }}
            </p>
            <p class="mt-2 text-sm text-slate-700 leading-relaxed">
              {{ manifest.description ?? connector.displayName }}
            </p>
          </div>
          <button
            v-if="!connector.isInstalled"
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
    </template>

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
