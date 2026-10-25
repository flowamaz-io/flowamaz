<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import FmButton from '@/components/common/FmButton.vue';
import FmInput from '@/components/common/FmInput.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import { useAuth } from '@/composables/useAuth';
import { isOnboardingComplete } from '@/composables/useOnboarding';
import { toUserFacingError } from '@/utils/error.util';
import { APP_NAME } from '@/utils/constants';
import { ssoService } from '@/services/sso.service';
import { postLoginTarget } from '@/utils/redirect.util';

const router = useRouter();
const route = useRoute();
const auth = useAuth();

const email = ref('');
const orgSlug = ref('');
const password = ref('');
const submitting = ref(false);
const error = ref('');

// SSO: once the org is known, offer "Continue with SSO". If the org requires SSO, the password form is hidden
// behind an owner link (owners keep password sign-in so a broken IdP can't lock the organisation out).
const ssoProvider = ref<string | null>(null);
const ssoRequired = ref(false);
const showPassword = ref(false);
const checkingSso = ref(false);
let ssoTimer: ReturnType<typeof setTimeout> | undefined;

const providerLabel = (provider: string): string =>
  provider === 'saml' || provider === 'oidc' ? 'SSO' : provider;

const passwordVisible = computed(() => !ssoProvider.value || !ssoRequired.value || showPassword.value);

async function checkSso(): Promise<void> {
  ssoProvider.value = null;
  ssoRequired.value = false;
  const slug = orgSlug.value.trim();
  if (!slug) return;
  checkingSso.value = true;
  try {
    const status = await ssoService.getStatus(slug);
    ssoProvider.value = status.enabled ? status.provider : null;
    ssoRequired.value = status.enabled && Boolean(status.required);
  } catch {
    ssoProvider.value = null;
  } finally {
    checkingSso.value = false;
  }
}

function onOrgInput(): void {
  ssoProvider.value = null;
  ssoRequired.value = false;
  showPassword.value = false;
  if (ssoTimer) clearTimeout(ssoTimer);
  ssoTimer = setTimeout(checkSso, 400);
}

function continueWithSso(): void {
  if (!ssoProvider.value) return;
  const returnTo = typeof route.query.redirect === 'string' ? route.query.redirect : undefined;
  window.location.href = ssoService.initiateUrl(orgSlug.value.trim(), ssoProvider.value, returnTo);
}

watch(orgSlug, onOrgInput);

// Coming back from a refused SSO sign-in: /login?org=acme&sso_error=CODE. Only known codes are shown, as our own
// words, so a crafted link can't put arbitrary text on the sign-in page.
const SSO_ERRORS: Record<string, string> = {
  SSO_RESPONSE_REJECTED: "Your identity provider's sign-in couldn't be verified. Try again; if it keeps failing, ask your administrator to check the SSO settings.",
  SSO_SIGNIN_EXPIRED: 'That sign-in took too long or was already used. Start again with "Continue with SSO".',
  SSO_NO_ACCOUNT: "You signed in with your identity provider, but you don't have a Flowamaz account in this organisation yet. Ask your administrator to invite you.",
  SSO_ACCOUNT_DEACTIVATED: 'Your Flowamaz account in this organisation is deactivated. Ask your administrator to reactivate it.',
  SSO_NOT_ON_PLAN: 'Single sign-on is part of the Enterprise plan, and this organisation is on a different plan. Sign in with your password or ask your organisation owner.',
  SSO_NOT_ENABLED: "Single sign-on isn't turned on for this organisation. Sign in with your email and password.",
  SSO_IDP_UNREACHABLE: "Flowamaz couldn't reach your identity provider. Try again in a minute.",
  SSO_IDP_MISCONFIGURED: "Your organisation's SSO settings don't match the identity provider. Ask your administrator to check them.",
};

onMounted(() => {
  if (typeof route.query.org === 'string' && route.query.org) orgSlug.value = route.query.org;
  const code = route.query.sso_error;
  if (typeof code === 'string' && code) {
    error.value = SSO_ERRORS[code] ?? 'Single sign-on didn\'t complete. Try again, or ask your administrator to check the SSO settings.';
  }
});

defineExpose({ checkSso, orgSlug });

async function submit(): Promise<void> {
  error.value = '';
  if (!email.value || !orgSlug.value || !password.value) {
    error.value = 'Enter your email, organisation URL, and password to sign in.';
    return;
  }
  submitting.value = true;
  try {
    await auth.login({ email: email.value.trim(), password: password.value, orgSlug: orgSlug.value.trim() });
    const orgId = auth.user.value?.orgId ?? '';
    await router.push(postLoginTarget(route.query.redirect, isOnboardingComplete(orgId, auth.workspaces.value.length)));
  } catch (e) {
    error.value = toUserFacingError(e).message;
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <div class="flex min-h-full items-center justify-center bg-slate-50 px-4 py-12">
    <div class="w-full max-w-md">
      <div class="mb-8 flex flex-col items-center">
        <div class="flex h-12 w-12 items-center justify-center rounded-xl bg-primary-600 text-xl font-bold text-white">
          F
        </div>
        <h1 class="mt-4 text-xl font-semibold text-slate-900">
          Sign in to your organisation
        </h1>
        <p class="mt-1 text-sm text-slate-500">
          Welcome back to {{ APP_NAME }}.
        </p>
      </div>

      <form
        class="rounded-xl border border-slate-200 bg-white p-6 shadow-sm"
        @submit.prevent="submit"
      >
        <FmAlert
          v-if="error"
          type="error"
          class="mb-4"
        >
          {{ error }}
        </FmAlert>

        <div class="space-y-4">
          <FmInput
            v-model="email"
            type="email"
            label="Email"
            placeholder="you@company.com"
            autocomplete="email"
            help="The email you signed up with. Unique within your organisation."
            help-article="getting-started/cloud-signup"
          />
          <FmInput
            v-model="orgSlug"
            label="Organisation URL"
            prefix="flowamaz.io/"
            placeholder="acme"
            help="Your organisation's unique slug, chosen at signup."
          />
          <FmInput
            v-if="passwordVisible"
            v-model="password"
            type="password"
            label="Password"
            placeholder="••••••••"
            autocomplete="current-password"
          />
        </div>

        <div
          v-if="passwordVisible"
          class="mt-2 text-right"
        >
          <RouterLink
            to="/forgot-password"
            class="text-sm text-primary-600 hover:text-primary-700"
          >
            Forgot password?
          </RouterLink>
        </div>

        <div
          v-if="ssoProvider"
          class="mt-4"
        >
          <p class="mb-2 text-center text-sm text-slate-500">
            {{ ssoRequired ? 'Your organisation signs in with single sign-on.' : 'Your organisation offers single sign-on.' }}
          </p>
          <FmButton
            type="button"
            block
            data-test="sso-button"
            @click="continueWithSso"
          >
            Continue with {{ providerLabel(ssoProvider) }}
          </FmButton>
          <button
            v-if="ssoRequired && !showPassword"
            type="button"
            class="mt-3 block w-full text-center text-sm text-primary-600 hover:text-primary-700"
            data-test="owner-password"
            @click="showPassword = true"
          >
            Organisation owner? Sign in with your password
          </button>
        </div>

        <FmButton
          v-if="passwordVisible"
          type="submit"
          :loading="submitting"
          block
          :variant="ssoProvider ? 'secondary' : undefined"
          class="mt-4"
        >
          Sign in with password
        </FmButton>
      </form>

      <p class="mt-6 text-center text-sm text-slate-500">
        New to Flowamaz?
        <RouterLink
          to="/register"
          class="font-medium text-primary-600 hover:text-primary-700"
        >
          Create organisation
        </RouterLink>
      </p>
    </div>
  </div>
</template>
