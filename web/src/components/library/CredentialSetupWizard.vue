<script setup lang="ts">
import { computed, ref, onBeforeUnmount } from 'vue';
import { Check, Loader2 } from 'lucide-vue-next';
import FmModal from '@/components/common/FmModal.vue';
import { connectorService } from '@/services/connector.service';
import { useWorkspace } from '@/composables/useWorkspace';
import type { ConnectorDefinition } from '@/services/connector.service';

interface ConnectorManifest {
  auth?: { type?: string };
  description?: string;
}

interface Props {
  connector: ConnectorDefinition;
  modelValue: boolean;
}

const props = defineProps<Props>();
const emit = defineEmits<{
  'update:modelValue': [value: boolean];
  installed: [credentialId: string];
}>();

const { currentWorkspaceId } = useWorkspace();

const step = ref<1 | 2 | 3>(1);
const apiKey = ref('');
const username = ref('');
const password = ref('');
const credentialName = ref('');
const oauthError = ref<string | null>(null);
const oauthConnecting = ref(false);
const resolvedCredentialId = ref('');
let oauthTimeout: ReturnType<typeof setTimeout> | null = null;

const manifest = computed<ConnectorManifest>(() => {
  try {
    return JSON.parse(props.connector.manifestJson) as ConnectorManifest;
  } catch {
    return {};
  }
});

const authType = computed<string>(() => manifest.value.auth?.type?.toLowerCase() ?? 'apikey');

const isOAuth = computed(() =>
  authType.value === 'oauth2' || authType.value === 'oauth',
);

const defaultCredentialName = computed(
  () => `${props.connector.displayName} workspace`,
);

function close(): void {
  emit('update:modelValue', false);
  resetState();
}

function resetState(): void {
  step.value = 1;
  apiKey.value = '';
  username.value = '';
  password.value = '';
  credentialName.value = '';
  oauthError.value = null;
  oauthConnecting.value = false;
  if (oauthTimeout) clearTimeout(oauthTimeout);
}

function canContinueStep1(): boolean {
  if (isOAuth.value) return false; // OAuth auto-advances
  if (authType.value === 'basic') return username.value.length > 0 && password.value.length > 0;
  return apiKey.value.length > 0;
}

function continueStep1(): void {
  if (canContinueStep1()) step.value = 2;
}

async function startOAuth(): Promise<void> {
  if (!currentWorkspaceId.value) return;
  oauthError.value = null;
  oauthConnecting.value = true;
  step.value = 2;

  try {
    const result = await connectorService.initiateOAuth(
      currentWorkspaceId.value,
      props.connector.connectorId,
    );

    const popup = window.open(result.authorizationUrl, 'oauth_popup', 'width=600,height=700');

    const handler = (event: MessageEvent): void => {
      if (event.data?.type !== 'oauth_complete') return;
      window.removeEventListener('message', handler);
      if (oauthTimeout) clearTimeout(oauthTimeout);

      if (event.data.success === true && typeof event.data.credentialId === 'string') {
        resolvedCredentialId.value = event.data.credentialId as string;
        credentialName.value = defaultCredentialName.value;
        oauthConnecting.value = false;
        step.value = 3;
      } else {
        oauthError.value = 'OAuth authorisation failed. Please try again.';
        oauthConnecting.value = false;
        step.value = 1;
      }
    };

    window.addEventListener('message', handler);

    oauthTimeout = setTimeout(() => {
      window.removeEventListener('message', handler);
      oauthConnecting.value = false;
      oauthError.value = 'OAuth timed out — please try again.';
      step.value = 1;
      popup?.close();
    }, 5 * 60 * 1000);
  } catch (err: unknown) {
    oauthConnecting.value = false;
    oauthError.value = err instanceof Error ? err.message : 'Failed to start OAuth flow.';
    step.value = 1;
  }
}

function finishStep3(): void {
  emit('installed', resolvedCredentialId.value);
  close();
}

onBeforeUnmount(() => {
  if (oauthTimeout) clearTimeout(oauthTimeout);
});
</script>

<template>
  <FmModal
    :model-value="modelValue"
    size="md"
    :title="`Connect ${connector.displayName}`"
    @update:model-value="close"
  >
    <!-- Progress indicator -->
    <div class="mb-6 flex items-center gap-2">
      <div
        v-for="n in 3"
        :key="n"
        :class="[
          'h-1.5 flex-1 rounded-full transition-colors',
          n <= step ? 'bg-indigo-600' : 'bg-slate-200',
        ]"
      />
    </div>

    <!-- Step 1: Auth method -->
    <Transition
      enter-active-class="transition-opacity duration-150"
      enter-from-class="opacity-0"
      leave-active-class="transition-opacity duration-100"
      leave-to-class="opacity-0"
      mode="out-in"
    >
      <div
        v-if="step === 1"
        key="step1"
        class="space-y-4"
      >
        <p class="text-sm text-slate-600">
          <span class="font-medium">Auth type:</span>
          <span class="ml-1 capitalize text-slate-800">{{ authType }}</span>
        </p>

        <div
          v-if="oauthError"
          class="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700"
        >
          {{ oauthError }}
        </div>

        <!-- OAuth -->
        <button
          v-if="isOAuth"
          class="w-full rounded-lg bg-indigo-600 py-2.5 text-sm font-semibold text-white hover:bg-indigo-700 transition-colors"
          @click="startOAuth"
        >
          Connect with {{ connector.displayName }}
        </button>

        <!-- API key -->
        <div
          v-else-if="authType === 'apikey'"
          class="space-y-3"
        >
          <label class="block">
            <span class="text-sm font-medium text-slate-700">API Key</span>
            <input
              v-model="apiKey"
              type="password"
              placeholder="Enter your API key"
              class="mt-1 w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200"
            >
          </label>
          <button
            :disabled="!canContinueStep1()"
            class="w-full rounded-lg bg-indigo-600 py-2.5 text-sm font-semibold text-white hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-50 transition-colors"
            @click="continueStep1"
          >
            Continue
          </button>
        </div>

        <!-- Basic auth -->
        <div
          v-else
          class="space-y-3"
        >
          <label class="block">
            <span class="text-sm font-medium text-slate-700">Username</span>
            <input
              v-model="username"
              type="text"
              placeholder="Username"
              class="mt-1 w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200"
            >
          </label>
          <label class="block">
            <span class="text-sm font-medium text-slate-700">Password</span>
            <input
              v-model="password"
              type="password"
              placeholder="Password"
              class="mt-1 w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200"
            >
          </label>
          <button
            :disabled="!canContinueStep1()"
            class="w-full rounded-lg bg-indigo-600 py-2.5 text-sm font-semibold text-white hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-50 transition-colors"
            @click="continueStep1"
          >
            Continue
          </button>
        </div>
      </div>

      <!-- Step 2: OAuth waiting -->
      <div
        v-else-if="step === 2"
        key="step2"
        class="flex flex-col items-center gap-4 py-6"
      >
        <Loader2 class="h-8 w-8 animate-spin text-indigo-600" />
        <p class="text-sm font-medium text-slate-700">
          Connecting to {{ connector.displayName }}…
        </p>
        <p class="text-xs text-slate-400">
          Complete the authorisation in the popup window.
        </p>
      </div>

      <!-- Step 3: Success -->
      <div
        v-else-if="step === 3"
        key="step3"
        class="space-y-4"
      >
        <div class="flex flex-col items-center gap-2 py-4">
          <div class="flex h-12 w-12 items-center justify-center rounded-full bg-green-100">
            <Check class="h-6 w-6 text-green-600" />
          </div>
          <p class="font-semibold text-slate-900">
            Connected to {{ connector.displayName }}!
          </p>
        </div>

        <label class="block">
          <span class="text-sm font-medium text-slate-700">Credential name</span>
          <input
            v-model="credentialName"
            type="text"
            :placeholder="defaultCredentialName"
            class="mt-1 w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200"
          >
        </label>

        <button
          class="w-full rounded-lg bg-indigo-600 py-2.5 text-sm font-semibold text-white hover:bg-indigo-700 transition-colors"
          @click="finishStep3"
        >
          Done
        </button>
      </div>
    </Transition>
  </FmModal>
</template>
