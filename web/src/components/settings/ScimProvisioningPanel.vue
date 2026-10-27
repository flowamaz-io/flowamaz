<script setup lang="ts">
import { onMounted, ref } from 'vue';
import dayjs from 'dayjs';
import { Copy, KeyRound, Plus, Trash2 } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmInput from '@/components/common/FmInput.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { ssoService, type ScimToken } from '@/services/sso.service';

// SCIM provisioning: the URL and tokens an identity provider uses to create, update and remove people and groups.

const props = defineProps<{ orgId: string }>();
const toast = useToast();

const loading = ref(true);
const loadError = ref('');
const baseUrl = ref('');
const tokens = ref<ScimToken[]>([]);
const newName = ref('');
const creating = ref(false);
const revoking = ref<string | null>(null);
/** The token just created. Kept only in memory, until the owner dismisses it. */
const justCreated = ref<string | null>(null);

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = '';
  try {
    const setup = await ssoService.getScim(props.orgId);
    baseUrl.value = setup.scimBaseUrl;
    tokens.value = setup.tokens;
  } catch (e) {
    loadError.value = toUserFacingError(e).message;
  } finally {
    loading.value = false;
  }
}

onMounted(load);

async function create(): Promise<void> {
  creating.value = true;
  try {
    const created = await ssoService.createScimToken(props.orgId, newName.value.trim() || 'SCIM token');
    tokens.value = [...tokens.value, created.token];
    baseUrl.value = created.scimBaseUrl;
    justCreated.value = created.plainToken;
    newName.value = '';
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    creating.value = false;
  }
}

async function revoke(token: ScimToken): Promise<void> {
  revoking.value = token.id;
  try {
    await ssoService.revokeScimToken(props.orgId, token.id);
    tokens.value = tokens.value.filter((t) => t.id !== token.id);
    toast.success(`Revoked "${token.name}". Your identity provider can no longer use it.`);
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    revoking.value = null;
  }
}

async function copy(text: string, what: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(text);
    toast.success(`${what} copied.`);
  } catch {
    toast.error(`Couldn't copy the ${what.toLowerCase()}. Select it and copy it yourself.`);
  }
}

const when = (iso: string | null) => (iso ? dayjs(iso).format('D MMM YYYY, HH:mm') : 'Never');
</script>

<template>
  <section
    class="space-y-4 rounded-xl border border-slate-200 bg-white p-6"
    aria-labelledby="scim-heading"
  >
    <div>
      <h2
        id="scim-heading"
        class="text-sm font-semibold text-slate-900"
      >
        SCIM provisioning
      </h2>
      <p class="mt-1 text-xs text-slate-500">
        Let your identity provider create, update and deactivate people, and keep their groups in step. To have roles
        follow SCIM groups, turn on "Take roles from SCIM groups" above. Organisation owners, and anyone who signs in with
        a password, can't have their email changed over SCIM, and owners can't be deactivated.
      </p>
    </div>

    <div
      v-if="loading"
      class="flex justify-center py-6 text-primary-600"
    >
      <FmSpinner />
    </div>

    <FmErrorState
      v-else-if="loadError"
      title="Couldn't load SCIM tokens"
      :description="loadError"
      action-label="Try again"
      @action="load"
    />

    <template v-else>
      <div class="flex items-center gap-2 rounded-lg bg-slate-50 p-3 text-xs">
        <span class="w-28 shrink-0 text-slate-600">SCIM base URL</span>
        <span
          class="min-w-0 flex-1 break-all font-mono"
          data-test="scim-base-url"
        >{{ baseUrl }}</span>
        <button
          type="button"
          class="text-slate-500 hover:text-slate-800"
          aria-label="Copy SCIM base URL"
          @click="copy(baseUrl, 'SCIM base URL')"
        >
          <Copy class="h-3.5 w-3.5" />
        </button>
      </div>

      <FmAlert
        v-if="justCreated"
        type="warning"
      >
        <p class="text-sm font-medium">
          Copy this token now: you won't see it again
        </p>
        <p class="mt-1 text-xs">
          Paste it into your identity provider as the bearer token (Okta: "API Token"; Entra: "Secret Token").
        </p>
        <div class="mt-2 flex items-center gap-2">
          <code
            class="min-w-0 flex-1 break-all rounded bg-white px-2 py-1 font-mono text-xs"
            data-test="scim-new-token"
          >{{ justCreated }}</code>
          <FmButton
            variant="secondary"
            @click="copy(justCreated, 'Token')"
          >
            Copy
          </FmButton>
          <FmButton
            variant="ghost"
            @click="justCreated = null"
          >
            Done
          </FmButton>
        </div>
      </FmAlert>

      <p
        v-if="tokens.length === 0"
        class="rounded-lg bg-slate-50 p-4 text-sm text-slate-600"
      >
        No SCIM tokens yet. Create one, then enter the base URL and the token in your identity provider's provisioning
        settings to start syncing people and groups.
      </p>
      <ul
        v-else
        class="divide-y divide-slate-100 rounded-lg border border-slate-200"
      >
        <li
          v-for="t in tokens"
          :key="t.id"
          class="flex flex-wrap items-center gap-3 px-3 py-2 text-sm"
        >
          <KeyRound class="h-4 w-4 text-slate-400" />
          <span class="font-medium text-slate-800">{{ t.name }}</span>
          <span class="font-mono text-xs text-slate-500">{{ t.tokenPrefix }}…</span>
          <span class="text-xs text-slate-500">Created {{ when(t.createdAt) }} · Last used {{ when(t.lastUsedAt) }}</span>
          <button
            type="button"
            class="ml-auto rounded-md p-1.5 text-slate-500 hover:bg-slate-100 hover:text-red-600 disabled:opacity-50"
            :aria-label="`Revoke ${t.name}`"
            :disabled="revoking === t.id"
            @click="revoke(t)"
          >
            <Trash2 class="h-4 w-4" />
          </button>
        </li>
      </ul>

      <div class="flex flex-wrap items-end gap-2">
        <div class="min-w-0 flex-1">
          <FmInput
            v-model="newName"
            label="New token name"
            placeholder="Okta production"
            autocomplete="off"
          />
        </div>
        <FmButton
          :loading="creating"
          @click="create"
        >
          <template #icon-left>
            <Plus class="h-4 w-4" />
          </template>
          Create token
        </FmButton>
      </div>
    </template>
  </section>
</template>
