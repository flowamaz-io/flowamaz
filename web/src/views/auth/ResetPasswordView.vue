<script setup lang="ts">
import { computed, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import FmButton from '@/components/common/FmButton.vue';
import FmInput from '@/components/common/FmInput.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import { authService } from '@/services/auth.service';
import { toUserFacingError } from '@/utils/error.util';

const route = useRoute();
const router = useRouter();

const token = (route.query.token as string) ?? '';
const orgSlug = (route.query.org as string) ?? '';

const newPassword = ref('');
const confirmPassword = ref('');
const submitting = ref(false);
const done = ref(false);
const error = ref('');

const passwordsMatch = computed(
  () => !confirmPassword.value || newPassword.value === confirmPassword.value,
);
const canSubmit = computed(
  () => newPassword.value.length >= 8 && newPassword.value === confirmPassword.value,
);

async function submit(): Promise<void> {
  error.value = '';
  if (!canSubmit.value) {
    error.value = 'Enter a matching password of at least 8 characters.';
    return;
  }
  submitting.value = true;
  try {
    await authService.resetPassword({ token, orgSlug, newPassword: newPassword.value });
    done.value = true;
    setTimeout(() => router.push('/login'), 2000);
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
          Choose a new password
        </h1>
      </div>

      <div class="rounded-xl border border-slate-200 bg-white p-6 shadow-sm">
        <div
          v-if="done"
          class="text-center"
        >
          <p class="text-sm text-slate-700">
            Password reset. Redirecting to sign-in…
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
              v-model="newPassword"
              type="password"
              label="New password"
              placeholder="At least 8 characters"
              autocomplete="new-password"
              help="Minimum 8 characters with at least one uppercase letter and one number."
            />
            <div>
              <FmInput
                v-model="confirmPassword"
                type="password"
                label="Confirm new password"
                placeholder="••••••••"
                autocomplete="new-password"
              />
              <p
                v-if="confirmPassword && !passwordsMatch"
                class="mt-1 text-xs text-danger-600"
              >
                Passwords do not match.
              </p>
            </div>
          </div>

          <FmButton
            type="submit"
            :loading="submitting"
            :disabled="!canSubmit"
            block
            class="mt-6"
          >
            Reset password
          </FmButton>
        </form>
      </div>

      <p class="mt-6 text-center text-sm text-slate-500">
        <RouterLink
          to="/forgot-password"
          class="font-medium text-primary-600 hover:text-primary-700"
        >
          Request a new reset link
        </RouterLink>
      </p>
    </div>
  </div>
</template>
