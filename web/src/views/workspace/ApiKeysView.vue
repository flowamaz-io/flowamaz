<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
import { KeyRound, Copy, Check } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmInput from '@/components/common/FmInput.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import FmModal from '@/components/common/FmModal.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmTooltip from '@/components/common/FmTooltip.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import { useWorkspace } from '@/composables/useWorkspace';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { fromNow } from '@/utils/date.util';
import { API_KEY_SCOPES } from '@/utils/constants';
import type { ApiKeyResponse } from '@/types';

const ws = useWorkspace();
const toast = useToast();

const loading = ref(true);
const loadError = ref('');

const createOpen = ref(false);
const keyName = ref('');
const environmentId = ref('');
const selectedScopes = ref<string[]>(['workflows:read']);
const creating = ref(false);

const newPlainKey = ref<string | null>(null);
const copied = ref(false);

const revokeTarget = ref<ApiKeyResponse | null>(null);
const revoking = ref(false);

// Environments come from GET /workspaces/{id}/environments (Dev / Staging / Production).
const environmentName = (id: string): string =>
  ws.environments.value.find((e) => e.id === id)?.name ?? 'Unknown';

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = '';
  try {
    await Promise.all([ws.loadApiKeys(), ws.loadEnvironments()]);
    if (!environmentId.value && ws.environments.value.length > 0) {
      environmentId.value = ws.environments.value[0]!.id;
    }
  } catch (e) {
    loadError.value = toUserFacingError(e).message;
  } finally {
    loading.value = false;
  }
}

onMounted(load);
watch(() => ws.currentWorkspaceId.value, load);

function toggleScope(scope: string): void {
  selectedScopes.value = selectedScopes.value.includes(scope)
    ? selectedScopes.value.filter((s) => s !== scope)
    : [...selectedScopes.value, scope];
}

async function submitCreate(): Promise<void> {
  if (!keyName.value.trim()) {
    toast.error('Give your key a recognisable name, e.g. "CI pipeline".');
    return;
  }
  if (!environmentId.value.trim()) {
    toast.error('Select or paste the environment ID this key belongs to.');
    return;
  }
  if (selectedScopes.value.length === 0) {
    toast.error('Pick at least one scope so the key can do something.');
    return;
  }
  creating.value = true;
  try {
    const created = await ws.createApiKey({
      name: keyName.value.trim(),
      environmentId: environmentId.value.trim(),
      scopes: selectedScopes.value,
      expiresAt: null,
    });
    newPlainKey.value = created.plainKey;
    createOpen.value = false;
    keyName.value = '';
    selectedScopes.value = ['workflows:read'];
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    creating.value = false;
  }
}

async function copyKey(): Promise<void> {
  if (!newPlainKey.value) return;
  await navigator.clipboard.writeText(newPlainKey.value);
  copied.value = true;
  toast.success('API key copied to clipboard.');
  window.setTimeout(() => (copied.value = false), 2000);
}

async function confirmRevoke(): Promise<void> {
  if (!revokeTarget.value) return;
  revoking.value = true;
  try {
    await ws.revokeApiKey(revokeTarget.value.id);
    toast.success('API key revoked.');
    revokeTarget.value = null;
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    revoking.value = false;
  }
}
</script>

<template>
  <div class="mx-auto max-w-4xl space-y-6 p-6">
    <header class="flex items-center justify-between">
      <div>
        <h1 class="flex items-center gap-2 text-2xl font-semibold text-slate-900">
          API keys
          <FmTooltip
            text="Use API keys to trigger workflows from external systems."
            label="About API keys"
          />
        </h1>
        <p class="text-sm text-slate-500">
          Programmatic access for triggering workflows and reading instances.
        </p>
      </div>
      <FmButton @click="createOpen = true">
        <template #icon-left>
          <KeyRound class="h-4 w-4" />
        </template>
        Create key
      </FmButton>
    </header>

    <FmAlert
      v-if="newPlainKey"
      type="warning"
      dismissible
    >
      <p class="font-medium">
        Copy your new API key now — it won't be shown again.
      </p>
      <div class="mt-2 flex items-center gap-2">
        <code class="flex-1 break-all rounded bg-white/70 px-2 py-1 text-xs">{{ newPlainKey }}</code>
        <button
          type="button"
          class="rounded-lg border border-amber-300 bg-white p-1.5 hover:bg-amber-50"
          aria-label="Copy key"
          @click="copyKey"
        >
          <Check
            v-if="copied"
            class="h-4 w-4 text-primary-600"
          />
          <Copy
            v-else
            class="h-4 w-4 text-amber-700"
          />
        </button>
      </div>
    </FmAlert>

    <div
      v-if="loading"
      class="flex justify-center py-16 text-primary-600"
    >
      <FmSpinner size="lg" />
    </div>

    <FmErrorState
      v-else-if="loadError"
      title="Couldn't load API keys"
      :description="loadError"
      action-label="Try again"
      help-article="workspaces/api-keys"
      @action="load"
    />

    <FmEmptyState
      v-else-if="ws.apiKeys.value.length === 0"
      :icon="KeyRound"
      title="No API keys yet"
      description="Create a key to call the Flowamaz API from your CI pipeline, scripts, or other systems."
      cta-label="Create your first API key"
      @cta="createOpen = true"
    />

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
              Prefix
            </th>
            <th class="px-4 py-3">
              Environment
            </th>
            <th class="px-4 py-3">
              Scopes
            </th>
            <th class="px-4 py-3">
              Last used
            </th>
            <th class="px-4 py-3">
              Status
            </th>
            <th class="px-4 py-3 text-right">
              Actions
            </th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100">
          <tr
            v-for="k in ws.apiKeys.value"
            :key="k.id"
          >
            <td class="px-4 py-3 font-medium text-slate-800">
              {{ k.name }}
            </td>
            <td class="px-4 py-3">
              <code class="text-xs text-slate-500">{{ k.keyPrefix }}…</code>
            </td>
            <td class="px-4 py-3">
              <FmBadge variant="slate">
                {{ environmentName(k.environmentId) }}
              </FmBadge>
            </td>
            <td class="px-4 py-3">
              <div class="flex flex-wrap gap-1">
                <FmBadge
                  v-for="s in k.scopes"
                  :key="s"
                  variant="slate"
                >
                  {{ s }}
                </FmBadge>
              </div>
            </td>
            <td class="px-4 py-3 text-slate-500">
              {{ fromNow(k.lastUsedAt) }}
            </td>
            <td class="px-4 py-3">
              <FmBadge :status="k.isActive ? 'active' : 'inactive'" />
            </td>
            <td class="px-4 py-3 text-right">
              <button
                v-if="k.isActive"
                type="button"
                class="text-sm font-medium text-danger-600 hover:text-danger-700"
                @click="revokeTarget = k"
              >
                Revoke
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Create modal -->
    <FmModal
      v-model="createOpen"
      title="Create API key"
      size="md"
    >
      <div class="space-y-4">
        <FmInput
          v-model="keyName"
          label="Name"
          placeholder="e.g. CI pipeline"
          help="A label to help you recognise this key later."
          help-article="workspaces/api-keys"
        />
        <div>
          <label
            for="api-key-environment"
            class="mb-1 block text-sm font-medium text-slate-700"
          >Environment</label>
          <select
            id="api-key-environment"
            v-model="environmentId"
            class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
          >
            <option
              v-for="env in ws.environments.value"
              :key="env.id"
              :value="env.id"
            >
              {{ env.name }}
            </option>
          </select>
          <p class="mt-1 text-xs text-slate-500">
            Production keys are minted as <code>fmz_live_*</code>; Dev and Staging as <code>fmz_test_*</code>.
          </p>
        </div>
        <div>
          <label class="mb-1 block text-sm font-medium text-slate-700">Scopes</label>
          <div class="grid grid-cols-2 gap-2">
            <label
              v-for="scope in API_KEY_SCOPES"
              :key="scope.value"
              class="flex cursor-pointer items-center gap-2 rounded-lg border border-slate-200 px-3 py-2 text-sm"
            >
              <input
                type="checkbox"
                :checked="selectedScopes.includes(scope.value)"
                class="h-4 w-4 rounded border-slate-300 text-primary-600 focus:ring-primary-500"
                @change="toggleScope(scope.value)"
              >
              {{ scope.label }}
            </label>
          </div>
        </div>
      </div>
      <template #footer>
        <FmButton
          variant="secondary"
          @click="createOpen = false"
        >
          Cancel
        </FmButton>
        <FmButton
          :loading="creating"
          @click="submitCreate"
        >
          Create key
        </FmButton>
      </template>
    </FmModal>

    <!-- Revoke confirmation -->
    <FmModal
      :model-value="revokeTarget !== null"
      title="Revoke API key"
      size="sm"
      @update:model-value="revokeTarget = null"
    >
      <p class="text-sm text-slate-600">
        Revoke <strong>{{ revokeTarget?.name }}</strong>? Any system using this key will immediately lose access. This
        cannot be undone — you'll need to create a new key.
      </p>
      <template #footer>
        <FmButton
          variant="secondary"
          @click="revokeTarget = null"
        >
          Cancel
        </FmButton>
        <FmButton
          variant="danger"
          :loading="revoking"
          @click="confirmRevoke"
        >
          Revoke key
        </FmButton>
      </template>
    </FmModal>
  </div>
</template>
