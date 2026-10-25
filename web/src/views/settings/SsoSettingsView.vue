<script setup lang="ts">
import { computed, onMounted, ref, useId } from 'vue';
import { Copy, Plus, ShieldCheck, Trash2 } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmInput from '@/components/common/FmInput.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import { useAuth } from '@/composables/useAuth';
import { useToast } from '@/composables/useToast';
import { useWorkspaceStore } from '@/stores/workspace.store';
import { toUserFacingError } from '@/utils/error.util';
import { ssoService, type SsoConfig, type SsoGroupRoleMapping, type WorkspaceRoleName } from '@/services/sso.service';

const auth = useAuth();
const toast = useToast();
const workspaceStore = useWorkspaceStore();
const ids = { cert: useId(), require: useId(), jit: useId(), active: useId(), role: useId() };

const ROLES: WorkspaceRoleName[] = ['Viewer', 'Runner', 'Operator', 'Designer', 'Admin'];

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
const requireSso = ref(false);
const jitProvisioning = ref(true);
const defaultRole = ref<WorkspaceRoleName>('Viewer');
const groupsAttribute = ref('groups');
const mappings = ref<SsoGroupRoleMapping[]>([]);
const config = ref<SsoConfig | null>(null);

const planIncludesSso = computed(() => config.value?.planIncludesSso ?? true);
const configured = computed(() => Boolean(config.value?.hasCertificate || config.value?.hasClientSecret || config.value?.idpSsoUrl || config.value?.issuerUrl));

const statusLabel = computed(() => {
  if (!planIncludesSso.value) return 'Enterprise plan';
  if (!configured.value) return 'Not set up';
  if (!config.value?.isActive) return 'Set up, off';
  return config.value.requireSso ? 'On, required' : 'On';
});
const statusVariant = computed(() =>
  !planIncludesSso.value || !configured.value ? 'slate' : config.value?.isActive ? 'primary' : 'amber',
);

async function load(): Promise<void> {
  if (!orgId.value) return;
  loading.value = true;
  loadError.value = '';
  try {
    const [existing] = await Promise.all([
      ssoService.getConfig(orgId.value),
      workspaceStore.allWorkspaces.length ? Promise.resolve() : workspaceStore.loadWorkspaces().catch(() => undefined),
    ]);
    config.value = existing;
    if (existing) {
      provider.value = existing.provider === 'oidc' ? 'oidc' : 'saml';
      isActive.value = existing.isActive;
      idpEntityId.value = existing.idpEntityId ?? '';
      idpSsoUrl.value = existing.idpSsoUrl ?? '';
      issuerUrl.value = existing.issuerUrl ?? '';
      clientId.value = existing.clientId ?? '';
      requireSso.value = existing.requireSso;
      jitProvisioning.value = existing.jitProvisioning;
      defaultRole.value = existing.defaultRole;
      groupsAttribute.value = existing.groupsAttribute || 'groups';
      mappings.value = (existing.groupRoleMappings ?? []).map((m) => ({ ...m }));
    }
  } catch (e) {
    loadError.value = toUserFacingError(e).message;
  } finally {
    loading.value = false;
  }
}

onMounted(load);

function addMapping(): void {
  mappings.value.push({ group: '', workspaceId: null, role: 'Viewer' });
}

function removeMapping(index: number): void {
  mappings.value.splice(index, 1);
}

async function copy(text: string | null | undefined, what: string): Promise<void> {
  if (!text) return;
  try {
    await navigator.clipboard.writeText(text);
    toast.success(`${what} copied.`);
  } catch {
    toast.error(`Couldn't copy the ${what.toLowerCase()}. Select it and copy it yourself.`);
  }
}

async function save(): Promise<void> {
  const blank = mappings.value.findIndex((m) => !m.group.trim());
  if (blank >= 0) {
    toast.error(`Group mapping ${blank + 1} has no group name. Enter the IdP group, or remove the row.`);
    return;
  }
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
      requireSso: requireSso.value,
      jitProvisioning: jitProvisioning.value,
      defaultRole: defaultRole.value,
      groupsAttribute: groupsAttribute.value.trim() || 'groups',
      groupRoleMappings: mappings.value.map((m) => ({ ...m, group: m.group.trim() })),
    });
    requireSso.value = config.value.requireSso;
    idpCertificate.value = '';
    clientSecret.value = '';
    toast.success('SSO settings saved.');
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
    <header class="flex items-center justify-between gap-4">
      <div>
        <h1 class="text-2xl font-semibold text-slate-900">
          Single sign-on
        </h1>
        <p class="text-sm text-slate-500">
          Your team signs in through your identity provider (Okta, Entra ID, Google Workspace, ADFS…) with SAML 2.0 or OIDC.
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

    <FmAlert
      v-else-if="!planIncludesSso"
      type="info"
    >
      <p class="text-sm font-medium">
        Single sign-on is part of the Enterprise plan
      </p>
      <p class="mt-1 text-sm">
        Enterprise adds SAML and OIDC sign-in, required SSO, automatic accounts and group-based roles.
        <RouterLink
          to="/settings/billing"
          class="font-medium underline"
        >
          See plans in Billing
        </RouterLink>
        or contact sales to upgrade.
      </p>
    </FmAlert>

    <template v-else>
      <section class="space-y-4 rounded-xl border border-slate-200 bg-white p-6">
        <h2 class="text-sm font-semibold text-slate-900">
          Identity provider
        </h2>
        <div
          role="radiogroup"
          aria-label="Protocol"
          class="flex gap-2"
        >
          <button
            v-for="p in (['saml', 'oidc'] as const)"
            :key="p"
            type="button"
            role="radio"
            :aria-checked="provider === p"
            :class="[
              'rounded-lg border px-4 py-2 text-sm font-medium',
              provider === p ? 'border-primary-500 bg-primary-50 text-primary-700' : 'border-slate-300 text-slate-600',
            ]"
            @click="provider = p"
          >
            {{ p === 'saml' ? 'SAML 2.0' : 'OIDC' }}
          </button>
        </div>

        <FmAlert type="info">
          <p class="text-sm font-medium">
            Enter these in your identity provider
          </p>
          <dl class="mt-2 space-y-1 text-xs">
            <template v-if="provider === 'saml'">
              <div
                v-for="[label, value] in ([['Entity ID (audience)', config?.spEntityId], ['ACS (reply) URL', config?.acsUrl], ['Metadata URL', config?.metadataUrl]] as const)"
                :key="label"
                class="flex items-center gap-2"
              >
                <dt class="w-40 shrink-0 text-slate-600">
                  {{ label }}
                </dt>
                <dd class="min-w-0 flex-1 break-all font-mono">
                  {{ value }}
                </dd>
                <button
                  type="button"
                  class="text-slate-500 hover:text-slate-800"
                  :aria-label="`Copy ${label}`"
                  @click="copy(value, label)"
                >
                  <Copy class="h-3.5 w-3.5" />
                </button>
              </div>
              <p class="pt-1 text-slate-600">
                Send the email as the NameID (or an "email" attribute), sign assertions with SHA-256, and don't encrypt them.
              </p>
            </template>
            <div
              v-else
              class="flex items-center gap-2"
            >
              <dt class="w-40 shrink-0 text-slate-600">
                Redirect URI
              </dt>
              <dd class="min-w-0 flex-1 break-all font-mono">
                {{ config?.oidcRedirectUri }}
              </dd>
              <button
                type="button"
                class="text-slate-500 hover:text-slate-800"
                aria-label="Copy redirect URI"
                @click="copy(config?.oidcRedirectUri, 'Redirect URI')"
              >
                <Copy class="h-3.5 w-3.5" />
              </button>
            </div>
          </dl>
        </FmAlert>

        <template v-if="provider === 'saml'">
          <FmInput
            v-model="idpEntityId"
            label="IdP entity ID (issuer)"
            placeholder="http://www.okta.com/exk123…"
          />
          <FmInput
            v-model="idpSsoUrl"
            label="IdP sign-in URL"
            placeholder="https://acme.okta.com/app/…/sso/saml"
          />
          <div>
            <label
              :for="ids.cert"
              class="mb-1 block text-sm font-medium text-slate-700"
            >IdP signing certificate (PEM)</label>
            <textarea
              :id="ids.cert"
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
            placeholder="https://login.microsoftonline.com/<tenant>/v2.0"
            help="Endpoints and signing keys are discovered from the issuer."
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
      </section>

      <section class="space-y-4 rounded-xl border border-slate-200 bg-white p-6">
        <h2 class="text-sm font-semibold text-slate-900">
          Access
        </h2>
        <div class="flex items-start gap-2">
          <input
            :id="ids.active"
            v-model="isActive"
            type="checkbox"
            class="mt-0.5 h-4 w-4 rounded border-slate-300 text-primary-600 focus:ring-primary-500"
          >
          <label
            :for="ids.active"
            class="text-sm text-slate-700"
          >
            Turn on SSO for this organisation
          </label>
        </div>
        <div class="flex items-start gap-2">
          <input
            :id="ids.require"
            v-model="requireSso"
            type="checkbox"
            :disabled="!isActive"
            class="mt-0.5 h-4 w-4 rounded border-slate-300 text-primary-600 focus:ring-primary-500 disabled:opacity-50"
          >
          <label
            :for="ids.require"
            class="text-sm text-slate-700"
          >
            Require SSO
            <span class="block text-xs text-slate-500">
              Members can't sign in with a password. Organisation owners still can, so a broken IdP setup can't lock everyone out.
            </span>
          </label>
        </div>
        <div class="flex items-start gap-2">
          <input
            :id="ids.jit"
            v-model="jitProvisioning"
            type="checkbox"
            class="mt-0.5 h-4 w-4 rounded border-slate-300 text-primary-600 focus:ring-primary-500"
          >
          <label
            :for="ids.jit"
            class="text-sm text-slate-700"
          >
            Create accounts on first sign-in
            <span class="block text-xs text-slate-500">
              Off: only people who already have a Flowamaz account in this organisation can sign in.
            </span>
          </label>
        </div>
        <div>
          <label
            :for="ids.role"
            class="mb-1 block text-sm font-medium text-slate-700"
          >Role when no group mapping applies</label>
          <select
            :id="ids.role"
            v-model="defaultRole"
            class="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm"
          >
            <option
              v-for="r in ROLES"
              :key="r"
              :value="r"
            >
              {{ r }}
            </option>
          </select>
        </div>
      </section>

      <section class="space-y-4 rounded-xl border border-slate-200 bg-white p-6">
        <div>
          <h2 class="text-sm font-semibold text-slate-900">
            Roles from IdP groups
          </h2>
          <p class="mt-1 text-xs text-slate-500">
            With mappings, the IdP decides each person's role at every SSO sign-in (the highest matching role wins);
            role changes made in Flowamaz are replaced at their next sign-in. Owners are never changed, and a workspace's
            last Admin is never demoted.
          </p>
        </div>
        <FmInput
          v-model="groupsAttribute"
          label="Groups attribute / claim"
          placeholder="groups"
          help="The SAML attribute or OIDC claim that lists the person's groups."
        />

        <p
          v-if="mappings.length === 0"
          class="rounded-lg bg-slate-50 p-4 text-sm text-slate-600"
        >
          No mappings yet: everyone gets the role above. Add a mapping to give an IdP group a role, in one workspace or all of them.
        </p>
        <div
          v-for="(m, i) in mappings"
          :key="i"
          class="grid grid-cols-1 gap-2 sm:grid-cols-[1fr_1fr_8rem_auto] sm:items-end"
        >
          <FmInput
            v-model="m.group"
            :label="`IdP group ${i + 1}`"
            placeholder="Credit-Managers"
          />
          <select
            v-model="m.workspaceId"
            :aria-label="`Workspace for mapping ${i + 1}`"
            class="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm"
          >
            <option :value="null">
              All workspaces
            </option>
            <option
              v-for="w in workspaceStore.allWorkspaces"
              :key="w.id"
              :value="w.id"
            >
              {{ w.name }}
            </option>
          </select>
          <select
            v-model="m.role"
            :aria-label="`Role for mapping ${i + 1}`"
            class="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm"
          >
            <option
              v-for="r in ROLES"
              :key="r"
              :value="r"
            >
              {{ r }}
            </option>
          </select>
          <button
            type="button"
            class="justify-self-start rounded-md p-2 text-slate-500 hover:bg-slate-100 hover:text-red-600"
            :aria-label="`Remove mapping ${i + 1}`"
            @click="removeMapping(i)"
          >
            <Trash2 class="h-4 w-4" />
          </button>
        </div>
        <FmButton
          variant="secondary"
          @click="addMapping"
        >
          <template #icon-left>
            <Plus class="h-4 w-4" />
          </template>
          Add mapping
        </FmButton>
      </section>

      <div class="flex justify-end gap-2">
        <FmButton
          variant="secondary"
          :loading="testing"
          @click="test"
        >
          Check settings
        </FmButton>
        <FmButton
          :loading="saving"
          @click="save"
        >
          <template #icon-left>
            <ShieldCheck class="h-4 w-4" />
          </template>
          Save SSO settings
        </FmButton>
      </div>
    </template>
  </div>
</template>
