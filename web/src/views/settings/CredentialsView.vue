<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { LockKeyhole, Plug } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmInput from '@/components/common/FmInput.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import FmModal from '@/components/common/FmModal.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import FmTooltip from '@/components/common/FmTooltip.vue';
import { useWorkspace } from '@/composables/useWorkspace';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { fromNow } from '@/utils/date.util';
import { connectorService, type ConnectorDefinition } from '@/services/connector.service';
import {
  AUTH_TYPE_LABELS,
  credentialService,
  isOAuthConnector,
  manualCredentialTypes,
  parseManifest,
  type CredentialAlias,
  type ManualAuthType,
} from '@/services/credential.service';

const ws = useWorkspace();
const toast = useToast();

const loading = ref(true);
const loadError = ref('');
const credentials = ref<CredentialAlias[]>([]);
const connectors = ref<ConnectorDefinition[]>([]);

const addOpen = ref(false);
const connectorId = ref('');
const authType = ref<ManualAuthType | ''>('');
const name = ref('');
const secret = ref('');
const username = ref('');
const saving = ref(false);
const connectingOAuth = ref(false);

const revokeTarget = ref<CredentialAlias | null>(null);
const revoking = ref(false);

let oauthListener: ((e: MessageEvent) => void) | null = null;

const workspaceId = computed(() => ws.currentWorkspaceId.value ?? '');
const selectedConnector = computed(() => connectors.value.find((c) => c.connectorId === connectorId.value));
const manifest = computed(() => parseManifest(selectedConnector.value));
const oauth = computed(() => isOAuthConnector(manifest.value));
const manualTypes = computed(() => manualCredentialTypes(manifest.value));
// Connectors that take a credential at all (some, like Email or File system, need none).
const connectableConnectors = computed(() =>
  connectors.value.filter((c) => {
    const m = parseManifest(c);
    return isOAuthConnector(m) || manualCredentialTypes(m).length > 0;
  }),
);

function connectorName(id: string | null): string {
  return connectors.value.find((c) => c.connectorId === id)?.displayName ?? id ?? 'Unknown connector';
}

async function load(): Promise<void> {
  if (!workspaceId.value) return;
  loading.value = true;
  loadError.value = '';
  try {
    const [creds, all] = await Promise.all([
      credentialService.list(workspaceId.value),
      connectorService.getAllConnectors(workspaceId.value),
    ]);
    credentials.value = creds;
    connectors.value = all;
  } catch (e) {
    loadError.value = toUserFacingError(e).message;
  } finally {
    loading.value = false;
  }
}

onMounted(load);
watch(workspaceId, load);
onBeforeUnmount(() => {
  if (oauthListener) window.removeEventListener('message', oauthListener);
});

watch(connectorId, () => {
  authType.value = manualTypes.value[0] ?? '';
  secret.value = '';
  username.value = '';
});

function openAdd(preselect?: string): void {
  connectorId.value = preselect ?? '';
  name.value = '';
  secret.value = '';
  username.value = '';
  addOpen.value = true;
}

const valuePlaceholder = computed(() => {
  switch (authType.value) {
    case 'ConnectionString':
      return selectedConnector.value?.connectorId === 'mysql'
        ? 'Server=db.example.com;Database=app;User ID=flowamaz;Password=…'
        : 'Host=db.example.com;Database=app;Username=flowamaz;Password=…';
    case 'HmacSecret':
      return 'Shared signing secret';
    default:
      return 'Paste the secret value';
  }
});

const canSave = computed(
  () => !!connectorId.value && !!authType.value && name.value.trim().length > 0 && secret.value.length > 0
    && (authType.value !== 'BasicAuth' || username.value.trim().length > 0),
);

async function save(): Promise<void> {
  if (!canSave.value || !authType.value) return;
  saving.value = true;
  try {
    const value = authType.value === 'BasicAuth' ? `${username.value.trim()}:${secret.value}` : secret.value;
    await credentialService.create(workspaceId.value, {
      connectorId: connectorId.value,
      name: name.value.trim(),
      authType: authType.value,
      value,
    });
    secret.value = '';
    addOpen.value = false;
    toast.success('Credential saved. Its value is encrypted and can never be viewed again.');
    await load();
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    saving.value = false;
  }
}

async function connectWithOAuth(): Promise<void> {
  if (!connectorId.value) return;
  connectingOAuth.value = true;
  try {
    const { authorizationUrl } = await connectorService.initiateOAuth(workspaceId.value, connectorId.value);
    const popup = window.open(authorizationUrl, 'oauth_popup', 'width=600,height=700');
    if (oauthListener) window.removeEventListener('message', oauthListener);
    oauthListener = async (event: MessageEvent) => {
      // Only trust the sign-in popup we opened (the callback is served from the API origin).
      if (event.source !== popup || event.data?.type !== 'oauth_complete') return;
      window.removeEventListener('message', oauthListener!);
      oauthListener = null;
      connectingOAuth.value = false;
      if (event.data.success === true) {
        addOpen.value = false;
        toast.success(`${connectorName(connectorId.value)} connected.`);
        await load();
      } else {
        toast.error('The sign-in was not completed. Try again, and approve the requested permissions.');
      }
    };
    window.addEventListener('message', oauthListener);
    if (!popup) {
      connectingOAuth.value = false;
      toast.error('Your browser blocked the sign-in window. Allow pop-ups for this site and try again.');
    }
  } catch (e) {
    connectingOAuth.value = false;
    toast.error(toUserFacingError(e).message);
  }
}

async function confirmRevoke(): Promise<void> {
  if (!revokeTarget.value) return;
  revoking.value = true;
  try {
    await credentialService.revoke(workspaceId.value, revokeTarget.value.id);
    toast.success(`'${revokeTarget.value.name}' revoked. Workflows using it will stop until you pick another credential.`);
    revokeTarget.value = null;
    await load();
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
          Credentials
          <FmTooltip
            text="Secrets your workflows use to call other systems. Values are encrypted and can never be read back."
            label="About credentials"
          />
        </h1>
        <p class="text-sm text-slate-500">
          Connect accounts and store API keys once; select them in a workflow step's Credential field.
        </p>
      </div>
      <FmButton @click="openAdd()">
        <template #icon-left>
          <LockKeyhole class="h-4 w-4" />
        </template>
        Add credential
      </FmButton>
    </header>

    <div
      v-if="loading"
      class="flex justify-center py-16 text-primary-600"
    >
      <FmSpinner size="lg" />
    </div>

    <FmErrorState
      v-else-if="loadError"
      title="Couldn't load credentials"
      :description="loadError"
      action-label="Try again"
      @action="load"
    />

    <FmEmptyState
      v-else-if="credentials.length === 0"
      :icon="Plug"
      title="No credentials yet"
      description="Connect Slack, Microsoft 365 or GitHub, or store a database connection string or API key, so workflow steps can act on those systems."
      cta-label="Add your first credential"
      @cta="openAdd()"
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
              Connector
            </th>
            <th class="px-4 py-3">
              Type
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
            v-for="c in credentials"
            :key="c.id"
          >
            <td class="px-4 py-3 font-medium text-slate-800">
              {{ c.name }}
            </td>
            <td class="px-4 py-3 text-slate-600">
              {{ connectorName(c.connectorId) }}
            </td>
            <td class="px-4 py-3">
              <FmBadge variant="slate">
                {{ AUTH_TYPE_LABELS[c.authType] ?? c.authType }}
              </FmBadge>
            </td>
            <td class="px-4 py-3 text-slate-500">
              {{ c.lastUsedAt ? fromNow(c.lastUsedAt) : 'Never' }}
            </td>
            <td class="px-4 py-3">
              <FmBadge :status="c.isActive ? 'active' : 'inactive'" />
            </td>
            <td class="px-4 py-3 text-right">
              <button
                v-if="c.isActive"
                type="button"
                class="text-sm font-medium text-danger-600 hover:text-danger-700"
                @click="revokeTarget = c"
              >
                Revoke
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <FmModal
      v-model="addOpen"
      title="Add credential"
      size="md"
    >
      <div class="space-y-4">
        <div>
          <label
            for="cred-connector"
            class="mb-1 block text-sm font-medium text-slate-700"
          >Connector</label>
          <select
            id="cred-connector"
            v-model="connectorId"
            class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
          >
            <option value="">
              Choose a connector
            </option>
            <option
              v-for="c in connectableConnectors"
              :key="c.connectorId"
              :value="c.connectorId"
            >
              {{ c.displayName }}
            </option>
          </select>
        </div>

        <div
          v-if="selectedConnector && oauth"
          class="rounded-lg border border-slate-200 bg-slate-50 p-4"
        >
          <p class="text-sm text-slate-700">
            Sign in to {{ selectedConnector.displayName }} and approve access. The token is stored encrypted for this workspace.
          </p>
          <FmButton
            class="mt-3"
            :loading="connectingOAuth"
            @click="connectWithOAuth"
          >
            Connect with OAuth
          </FmButton>
          <p
            v-if="manualTypes.length > 0"
            class="mt-3 text-xs text-slate-500"
          >
            Or paste a token below instead.
          </p>
        </div>

        <template v-if="selectedConnector && manualTypes.length > 0">
          <div v-if="manualTypes.length > 1">
            <label
              for="cred-type"
              class="mb-1 block text-sm font-medium text-slate-700"
            >Type</label>
            <select
              id="cred-type"
              v-model="authType"
              class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
            >
              <option
                v-for="t in manualTypes"
                :key="t"
                :value="t"
              >
                {{ AUTH_TYPE_LABELS[t] ?? t }}
              </option>
            </select>
          </div>
          <FmInput
            v-model="name"
            label="Name"
            placeholder="e.g. Production database"
            help="Shown in workflow steps. Never contains the secret."
          />
          <FmInput
            v-if="authType === 'BasicAuth'"
            v-model="username"
            label="Username"
            autocomplete="off"
          />
          <FmInput
            v-model="secret"
            :label="authType === 'BasicAuth' ? 'Password' : (AUTH_TYPE_LABELS[authType] ?? 'Secret')"
            type="password"
            autocomplete="new-password"
            :placeholder="valuePlaceholder"
            hint="Stored encrypted. You won't be able to view it again."
          />
        </template>

        <p
          v-if="selectedConnector && !oauth && manualTypes.length === 0"
          class="text-sm text-slate-500"
        >
          {{ selectedConnector.displayName }} doesn't need a credential.
        </p>
      </div>
      <template #footer>
        <FmButton
          variant="secondary"
          @click="addOpen = false"
        >
          Cancel
        </FmButton>
        <FmButton
          v-if="manualTypes.length > 0"
          :disabled="!canSave"
          :loading="saving"
          @click="save"
        >
          Save credential
        </FmButton>
      </template>
    </FmModal>

    <FmModal
      :model-value="revokeTarget !== null"
      title="Revoke credential?"
      size="sm"
      @update:model-value="(v: boolean) => { if (!v) revokeTarget = null; }"
    >
      <p class="text-sm text-slate-600">
        Workflow steps that use <strong>{{ revokeTarget?.name }}</strong> will fail until they're pointed at another credential.
        This can't be undone.
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
          Revoke
        </FmButton>
      </template>
    </FmModal>
  </div>
</template>
