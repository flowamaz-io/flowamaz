<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { ShieldCheck } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmInput from '@/components/common/FmInput.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import { useAuth } from '@/composables/useAuth';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { ssoService, type SsoConfig } from '@/services/sso.service';

const auth = useAuth();
const toast = useToast();

const loading = ref(true);
const loadError = ref('');
const saving = ref(false);
const testing = ref(false);

const orgId = computed(() => auth.user.value?.orgId ?? '');

const provider = ref<'saml' | 'oidc'>('saml');
const isActive = ref(false);
const idpEntityId = ref('');
const idpSsoUrl = ref('');
const idpCertificate = ref('');
const issuerUrl = ref('');
const clientId = ref('');
const clientSecret = ref('');
const config = ref<SsoConfig | null>(null);

const statusLabel = computed(() => {
  if (!config.value) return 'Not configured';
  return config.value.isActive ? 'Configured + Active' : 'Configured but inactive';
});
const statusVariant = computed(() =>
  !config.value ? 'slate' : config.value.isActive ? 'green' : 'amber',
);

async function load(): Promise<void> {
  if (!orgId.value) return;
  loading.value = true;
  loadError.value = '';
  try {
    const existing = await ssoService.getConfig(orgId.value);
    config.value = existing;
    if (existing) {
      provider.value = existing.provider === 'oidc' ? 'oidc' : 'saml';
      isActive.value = existing.isActive;
      idpEntityId.value = existing.idpEntityId ?? '';
      idpSsoUrl.value = existing.idpSsoUrl ?? '';
      issuerUrl.value = existing.issuerUrl ?? '';
      clientId.value = existing.clientId ?? '';
    }
  } catch (e) {
    loadError.value = toUserFacingError(e).message;
  } finally {
    loading.value = false;
  }
}

onMounted(load);

async function save(): Promise<void> {
  saving.value = true;
  try {
    config.value = await ssoService.configure(orgId.value, {
      provider: provider.value,
      isActive: isActive.value,
      idpEntityId: idpEntityId.value || null,
      idpSsoUrl: idpSsoUrl.value || null,
      idpCertificate: idpCertificate.value || null,
      issuerUrl: issuerUrl.value || null,
      clientId: clientId.value || null,
      clientSecret: clientSecret.value || null,
      scopes: null,
    });
    idpCertificate.value = '';
    clientSecret.value = '';
    toast.success('SSO configuration saved.');
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    saving.value = false;
  }
}

async function test(): Promise<void> {
  testing.value = true;
  try {
    const result = await ssoService.test(orgId.value);
    if (result.ok) toast.success(result.message);
    else toast.warning(result.message);
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    testing.value = false;
  }
}
</script>

<template>
  <div class="mx-auto max-w-3xl space-y-6 p-6">
    <header class="flex items-center justify-between">
      <div>
        <h1 class="text-2xl font-semibold text-slate-900">
          Single sign-on
        </h1>
        <p class="text-sm text-slate-500">
          Let your team sign in through your identity provider via SAML 2.0 or OIDC.
        </p>
      </div>
      <FmBadge :variant="statusVariant">
        {{ statusLabel }}
      </FmBadge>
    </header>

    <div
      v-if="loading"
      class="flex justify-center py-16 text-primary-600"
    >
      <FmSpinner size="lg" />
    </div>

    <FmErrorState
      v-else-if="loadError"
      title="Couldn't load SSO settings"
      :description="loadError"
      action-label="Try again"
      @action="load"
    />

    <template v-else>
      <section class="space-y-4 rounded-xl border border-slate-200 bg-white p-6">
        <div>
          <label class="mb-1 block text-sm font-medium text-slate-700">Provider</label>
          <div class="flex gap-2">
            <button
              v-for="p in (['saml', 'oidc'] as const)"
              :key="p"
              type="button"
              :class="[
                'rounded-lg border px-4 py-2 text-sm font-medium',
                provider === p ? 'border-primary-500 bg-primary-50 text-primary-700' : 'border-slate-300 text-slate-600',
              ]"
              @click="provider = p"
            >
              {{ p === 'saml' ? 'SAML 2.0' : 'OIDC' }}
            </button>
          </div>
        </div>

        <template v-if="provider === 'saml'">
          <FmAlert
            v-if="config?.spEntityId"
            type="info"
          >
            <p class="text-sm font-medium">
              Give these to your IdP
            </p>
            <p class="mt-1 text-xs">
              SP Entity ID: <code class="break-all">{{ config.spEntityId }}</code>
            </p>
          </FmAlert>
          <FmInput
            v-model="idpEntityId"
            label="IdP Entity ID"
            placeholder="https://idp.example.com/entity"
          />
          <FmInput
            v-model="idpSsoUrl"
            label="IdP SSO URL"
            placeholder="https://idp.example.com/sso"
          />
          <div>
            <label class="mb-1 block text-sm font-medium text-slate-700">IdP signing certificate (PEM)</label>
            <textarea
              v-model="idpCertificate"
              rows="4"
              :placeholder="config?.hasCertificate ? 'A certificate is saved. Paste a new one to replace it.' : '-----BEGIN CERTIFICATE-----'"
              class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 font-mono text-xs focus:border-primary-500 focus:outline-none"
            />
          </div>
        </template>

        <template v-else>
          <FmInput
            v-model="issuerUrl"
            label="Issuer URL"
            placeholder="https://accounts.google.com"
            help="The OIDC configuration is auto-discovered from the issuer."
          />
          <FmInput
            v-model="clientId"
            label="Client ID"
            placeholder="your-client-id"
          />
          <FmInput
            v-model="clientSecret"
            type="password"
            label="Client secret"
            :placeholder="config?.hasClientSecret ? 'A secret is saved. Enter a new one to replace it.' : 'your-client-secret'"
          />
        </template>

        <label class="flex items-center gap-2 text-sm text-slate-700">
          <input
            v-model="isActive"
            type="checkbox"
            class="h-4 w-4 rounded border-slate-300 text-primary-600 focus:ring-primary-500"
          >
          Enable SSO for this organisation
        </label>

        <div class="flex justify-end gap-2 border-t border-slate-100 pt-4">
          <FmButton
            variant="secondary"
            :loading="testing"
            @click="test"
          >
            Test connection
          </FmButton>
          <FmButton
            :loading="saving"
            @click="save"
          >
            <template #icon-left>
              <ShieldCheck class="h-4 w-4" />
            </template>
            Save SSO
          </FmButton>
        </div>
      </section>
    </template>
  </div>
</template>
