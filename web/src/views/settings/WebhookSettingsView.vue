<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { Webhook, Copy, Check } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import FmModal from '@/components/common/FmModal.vue';
import CreateWebhookModal from '@/components/settings/CreateWebhookModal.vue';
import { useWorkspace } from '@/composables/useWorkspace';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { fromNow } from '@/utils/date.util';
import { workflowService } from '@/services/workflow.service';
import { webhookService, webhookUrl, type CreatedWebhook, type WebhookEndpoint } from '@/services/webhook.service';

const ws = useWorkspace();
const toast = useToast();

const loading = ref(true);
const loadError = ref('');
const endpoints = ref<WebhookEndpoint[]>([]);
const workflowNames = ref<Record<string, string>>({});

const createOpen = ref(false);
const newSecret = ref<CreatedWebhook | null>(null);
const copied = ref(false);

const deleteTarget = ref<WebhookEndpoint | null>(null);
const deleting = ref(false);
const rotating = ref<string | null>(null);

const workspaceId = computed(() => ws.currentWorkspaceId.value ?? '');

function workflowName(id: string): string {
  return workflowNames.value[id] ?? 'Unknown workflow';
}

async function load(): Promise<void> {
  if (!workspaceId.value) return;
  loading.value = true;
  loadError.value = '';
  try {
    const [list, page] = await Promise.all([
      webhookService.list(workspaceId.value),
      workflowService.list(workspaceId.value, 1, 100),
    ]);
    endpoints.value = list;
    workflowNames.value = Object.fromEntries(page.data.map((w) => [w.id, w.name]));
  } catch (e) {
    loadError.value = toUserFacingError(e).message;
  } finally {
    loading.value = false;
  }
}

onMounted(load);
watch(() => ws.currentWorkspaceId.value, load);

function onCreated(webhook: CreatedWebhook): void {
  newSecret.value = webhook;
  toast.success('Webhook created. Copy the secret now — it won\'t be shown again.');
  void load();
}

async function copySecret(): Promise<void> {
  if (!newSecret.value) return;
  await navigator.clipboard.writeText(newSecret.value.secret);
  copied.value = true;
  toast.success('Webhook secret copied to clipboard.');
  window.setTimeout(() => (copied.value = false), 2000);
}

async function rotate(endpoint: WebhookEndpoint): Promise<void> {
  rotating.value = endpoint.id;
  try {
    newSecret.value = await webhookService.rotate(workspaceId.value, endpoint.id);
    toast.success('Secret rotated. Update your sender with the new secret — the old one no longer works.');
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    rotating.value = null;
  }
}

async function confirmDelete(): Promise<void> {
  if (!deleteTarget.value) return;
  deleting.value = true;
  try {
    await webhookService.remove(workspaceId.value, deleteTarget.value.id);
    toast.success('Webhook deleted.');
    deleteTarget.value = null;
    await load();
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    deleting.value = false;
  }
}
</script>

<template>
  <div class="mx-auto max-w-4xl space-y-6 p-6">
    <header class="flex items-center justify-between">
      <div>
        <h1 class="text-2xl font-semibold text-slate-900">
          Webhooks
        </h1>
        <p class="text-sm text-slate-500">
          Trigger published workflows from external systems with a signed HTTP request.
        </p>
      </div>
      <FmButton @click="createOpen = true">
        <template #icon-left>
          <Webhook class="h-4 w-4" />
        </template>
        Create webhook
      </FmButton>
    </header>

    <FmAlert
      v-if="newSecret"
      :key="newSecret.endpointId"
      type="warning"
      dismissible
    >
      <p class="font-medium">
        Copy your signing secret now — it won't be shown again.
      </p>
      <p class="mt-1 text-xs">
        Sign the raw request body with HMAC-SHA256 using this secret and send it in the
        <code>X-Flowamaz-Signature</code> header.
      </p>
      <div class="mt-2 flex items-center gap-2">
        <code class="flex-1 break-all rounded bg-white/70 px-2 py-1 text-xs">{{ newSecret.secret }}</code>
        <button
          type="button"
          class="rounded-lg border border-amber-300 bg-white p-1.5 hover:bg-amber-50"
          aria-label="Copy secret"
          @click="copySecret"
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
      <p class="mt-2 text-xs">
        Endpoint URL: <code class="break-all">{{ webhookUrl(newSecret.endpointId) }}</code>
      </p>
    </FmAlert>

    <div
      v-if="loading"
      class="flex justify-center py-16 text-primary-600"
    >
      <FmSpinner size="lg" />
    </div>

    <FmErrorState
      v-else-if="loadError"
      title="Couldn't load webhooks"
      :description="loadError"
      action-label="Try again"
      @action="load"
    />

    <FmEmptyState
      v-else-if="endpoints.length === 0"
      :icon="Webhook"
      title="No webhooks yet"
      description="Create a webhook to let external systems trigger a published workflow with a signed HTTP request."
      cta-label="Create your first webhook"
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
              Workflow
            </th>
            <th class="px-4 py-3">
              Description
            </th>
            <th class="px-4 py-3">
              Endpoint URL
            </th>
            <th class="px-4 py-3">
              Created
            </th>
            <th class="px-4 py-3 text-right">
              Actions
            </th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100">
          <tr
            v-for="e in endpoints"
            :key="e.id"
          >
            <td class="px-4 py-3 font-medium text-slate-800">
              {{ workflowName(e.workflowDefinitionId) }}
            </td>
            <td class="px-4 py-3 text-slate-500">
              {{ e.description || '—' }}
            </td>
            <td class="px-4 py-3">
              <code class="text-xs text-slate-500">{{ webhookUrl(e.id) }}</code>
            </td>
            <td class="px-4 py-3 text-slate-500">
              {{ fromNow(e.createdAt) }}
            </td>
            <td class="px-4 py-3 text-right">
              <div class="flex justify-end gap-3">
                <button
                  type="button"
                  class="text-sm font-medium text-slate-600 hover:text-slate-800 disabled:opacity-50"
                  :disabled="rotating === e.id"
                  @click="rotate(e)"
                >
                  {{ rotating === e.id ? 'Rotating…' : 'Rotate' }}
                </button>
                <button
                  type="button"
                  class="text-sm font-medium text-danger-600 hover:text-danger-700"
                  @click="deleteTarget = e"
                >
                  Delete
                </button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <CreateWebhookModal
      v-model="createOpen"
      :workspace-id="workspaceId"
      @created="onCreated"
    />

    <FmModal
      :model-value="deleteTarget !== null"
      title="Delete webhook"
      size="sm"
      @update:model-value="deleteTarget = null"
    >
      <p class="text-sm text-slate-600">
        Delete this webhook for <strong>{{ deleteTarget ? workflowName(deleteTarget.workflowDefinitionId) : '' }}</strong>?
        Any system posting to this URL will immediately stop triggering the workflow. This cannot be undone.
      </p>
      <template #footer>
        <FmButton
          variant="secondary"
          @click="deleteTarget = null"
        >
          Cancel
        </FmButton>
        <FmButton
          variant="danger"
          :loading="deleting"
          @click="confirmDelete"
        >
          Delete webhook
        </FmButton>
      </template>
    </FmModal>
  </div>
</template>
