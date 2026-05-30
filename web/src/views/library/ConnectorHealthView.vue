<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import CredentialSetupWizard from '@/components/library/CredentialSetupWizard.vue';
import { connectorService } from '@/services/connector.service';
import { useWorkspace } from '@/composables/useWorkspace';
import type { ConnectorDefinition, ConnectorHealthStatus } from '@/services/connector.service';

const { currentWorkspaceId, loadWorkspaces } = useWorkspace();

const health = ref<ConnectorHealthStatus[]>([]);
const loading = ref(false);
const error = ref<string | null>(null);
let refreshTimer: ReturnType<typeof setInterval> | null = null;

const wizardOpen = ref(false);
const wizardConnector = ref<ConnectorDefinition | null>(null);

async function load(): Promise<void> {
  if (!currentWorkspaceId.value) await loadWorkspaces();
  if (!currentWorkspaceId.value) return;

  loading.value = health.value.length === 0;
  error.value = null;
  try {
    health.value = await connectorService.getConnectorHealth(currentWorkspaceId.value);
  } catch (err: unknown) {
    error.value =
      err instanceof Error
        ? err.message
        : 'Failed to load connector health. Check your network connection and try again.';
  } finally {
    loading.value = false;
  }
}

type StatusVariant = 'healthy' | 'expired' | 'rate_limited' | 'error';

function statusBadge(status: string): { dot: string; label: string; text: string } {
  const map: Record<StatusVariant, { dot: string; label: string; text: string }> = {
    healthy: { dot: 'bg-green-500', label: 'Healthy', text: 'text-green-700' },
    expired: { dot: 'bg-red-500', label: 'Expired', text: 'text-red-700' },
    rate_limited: { dot: 'bg-yellow-400', label: 'Rate Limited', text: 'text-yellow-700' },
    error: { dot: 'bg-orange-500', label: 'Error', text: 'text-orange-700' },
  };
  return map[status as StatusVariant] ?? { dot: 'bg-slate-400', label: status, text: 'text-slate-600' };
}

function needsReconnect(status: string): boolean {
  return status === 'expired' || status === 'error';
}

async function reconnect(item: ConnectorHealthStatus): Promise<void> {
  if (!currentWorkspaceId.value) return;
  try {
    const c = await connectorService.getConnectorById(
      currentWorkspaceId.value,
      item.connectorId,
    );
    wizardConnector.value = c;
    wizardOpen.value = true;
  } catch {
    // ignore — wizard won't open
  }
}

function onWizardClose(val: boolean): void {
  wizardOpen.value = val;
  if (!val) wizardConnector.value = null;
}

onMounted(() => {
  void load();
  refreshTimer = setInterval(() => { void load(); }, 30_000);
});

onUnmounted(() => {
  if (refreshTimer) clearInterval(refreshTimer);
});
</script>

<template>
  <div class="space-y-6">
    <!-- Header -->
    <div class="flex items-center justify-between">
      <div>
        <h1 class="text-xl font-semibold text-slate-900">
          Connector Health
        </h1>
        <p class="text-sm text-slate-500">
          Status of all installed connector credentials. Refreshes every 30 s.
        </p>
      </div>
    </div>

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
      title="Couldn't load health data"
      :description="error"
      action-label="Try again"
      @action="load"
    />

    <!-- Empty -->
    <FmEmptyState
      v-else-if="health.length === 0"
      title="No installed connectors"
      description="Install a connector from the Library to monitor its health here."
    />

    <!-- Table -->
    <div
      v-else
      class="overflow-hidden rounded-xl border border-slate-200 bg-white"
    >
      <table class="min-w-full divide-y divide-slate-200 text-sm">
        <thead class="bg-slate-50">
          <tr>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
              Connector
            </th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
              Credential
            </th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
              Status
            </th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
              Last Used
            </th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
              Calls / Hour
            </th>
            <th class="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
              Expires
            </th>
            <th class="px-4 py-3" />
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100">
          <tr
            v-for="item in health"
            :key="`${item.connectorId}-${item.credentialName}`"
            class="hover:bg-slate-50 transition-colors"
          >
            <td class="px-4 py-3 font-medium text-slate-800 font-mono text-xs">
              {{ item.connectorId }}
            </td>
            <td class="px-4 py-3 text-slate-600">
              {{ item.credentialName }}
            </td>
            <td class="px-4 py-3">
              <div class="flex items-center gap-1.5">
                <span :class="['h-2 w-2 rounded-full', statusBadge(item.status).dot]" />
                <span :class="['text-xs font-medium', statusBadge(item.status).text]">
                  {{ statusBadge(item.status).label }}
                </span>
              </div>
              <p
                v-if="item.errorMessage"
                class="mt-0.5 text-xs text-slate-400 truncate max-w-48"
                :title="item.errorMessage"
              >
                {{ item.errorMessage }}
              </p>
            </td>
            <td class="px-4 py-3 text-slate-500 text-xs">
              {{ item.lastUsedAt ? new Date(item.lastUsedAt).toLocaleString() : '—' }}
            </td>
            <td class="px-4 py-3 text-slate-600">
              {{ item.callsLastHour }}
            </td>
            <td class="px-4 py-3 text-slate-500 text-xs">
              {{ item.expiresAt ? new Date(item.expiresAt).toLocaleString() : '—' }}
            </td>
            <td class="px-4 py-3">
              <button
                v-if="needsReconnect(item.status)"
                class="rounded-md bg-indigo-50 px-3 py-1.5 text-xs font-semibold text-indigo-700 hover:bg-indigo-100 transition-colors"
                @click="reconnect(item)"
              >
                Reconnect
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Re-connect wizard -->
    <CredentialSetupWizard
      v-if="wizardConnector"
      :connector="wizardConnector"
      :model-value="wizardOpen"
      @update:model-value="onWizardClose"
      @installed="wizardOpen = false"
    />
  </div>
</template>
