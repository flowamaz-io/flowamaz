<script setup lang="ts">
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import FmButton from '@/components/common/FmButton.vue';
import FmInput from '@/components/common/FmInput.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import { useAuth } from '@/composables/useAuth';
import { useToast } from '@/composables/useToast';
import { isOnboardingComplete } from '@/composables/useOnboarding';
import { toUserFacingError } from '@/utils/error.util';
import { APP_NAME } from '@/utils/constants';

const router = useRouter();
const auth = useAuth();
const toast = useToast();

const email = ref('');
const orgSlug = ref('');
const password = ref('');
const submitting = ref(false);
const error = ref('');

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
    router.push(isOnboardingComplete(orgId) ? '/' : '/onboarding');
  } catch (e) {
    error.value = toUserFacingError(e).message;
  } finally {
    submitting.value = false;
  }
}

function forgotPassword(): void {
  toast.info('Password reset is coming soon. Contact your org owner if you are locked out.');
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
            v-model="password"
            type="password"
            label="Password"
            placeholder="••••••••"
            autocomplete="current-password"
          />
        </div>

        <div class="mt-2 text-right">
          <button
            type="button"
            class="text-sm text-primary-600 hover:text-primary-700"
            @click="forgotPassword"
          >
            Forgot password?
          </button>
        </div>

        <FmButton
          type="submit"
          :loading="submitting"
          block
          class="mt-4"
        >
          Sign in
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
