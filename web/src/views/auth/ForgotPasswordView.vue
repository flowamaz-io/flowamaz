<script setup lang="ts">
import { ref } from 'vue';
import FmButton from '@/components/common/FmButton.vue';
import FmInput from '@/components/common/FmInput.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import { authService } from '@/services/auth.service';
import { toUserFacingError } from '@/utils/error.util';
import { APP_NAME } from '@/utils/constants';

const email = ref('');
const orgSlug = ref('');
const submitting = ref(false);
const sent = ref(false);
const error = ref('');

async function submit(): Promise<void> {
  error.value = '';
  if (!email.value || !orgSlug.value) {
    error.value = 'Enter your email address and organisation URL to request a reset link.';
    return;
  }
  submitting.value = true;
  try {
    await authService.forgotPassword({ email: email.value.trim(), orgSlug: orgSlug.value.trim() });
    sent.value = true;
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
          Reset your password
        </h1>
        <p class="mt-1 text-sm text-slate-500">
          Enter your {{ APP_NAME }} account email and organisation URL.
        </p>
      </div>

      <div class="rounded-xl border border-slate-200 bg-white p-6 shadow-sm">
        <div
          v-if="sent"
          class="text-center"
        >
          <p class="text-sm text-slate-700">
            If that email exists, a reset link has been sent. Check your inbox.
          </p>
          <p class="mt-2 text-xs text-slate-500">
            The link expires in 1 hour. Check your spam folder if it doesn't arrive.
          </p>
        </div>

        <form
          v-else
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
            />
            <FmInput
              v-model="orgSlug"
              label="Organisation URL"
              prefix="flowamaz.io/"
              placeholder="acme"
              help="Your organisation's unique slug, chosen at signup."
            />
          </div>

          <FmButton
            type="submit"
            :loading="submitting"
            block
            class="mt-6"
          >
            Send reset link
          </FmButton>
        </form>
      </div>

      <p class="mt-6 text-center text-sm text-slate-500">
        Remember your password?
        <RouterLink
          to="/login"
          class="font-medium text-primary-600 hover:text-primary-700"
        >
          Sign in
        </RouterLink>
      </p>
    </div>
  </div>
</template>
