<script setup lang="ts">
import { computed, ref } from 'vue';
import { useRouter } from 'vue-router';
import FmButton from '@/components/common/FmButton.vue';
import FmInput from '@/components/common/FmInput.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import { useAuth } from '@/composables/useAuth';
import { toUserFacingError } from '@/utils/error.util';
import { passwordStrength, slugify } from '@/utils/string.util';
import { APP_NAME, PLANS } from '@/utils/constants';

const router = useRouter();
const auth = useAuth();

const plan = ref('starter');
const orgName = ref('');
const orgSlug = ref('');
const slugTouched = ref(false);
const fullName = ref('');
const email = ref('');
const password = ref('');
const submitting = ref(false);
const error = ref('');

const strength = computed(() => passwordStrength(password.value));
const strengthColor = computed(
  () => ['bg-slate-200', 'bg-danger-400', 'bg-amber-400', 'bg-primary-400', 'bg-primary-600'][strength.value.score],
);

function onOrgNameInput(value: string): void {
  orgName.value = value;
  if (!slugTouched.value) orgSlug.value = slugify(value);
}

async function submit(): Promise<void> {
  error.value = '';
  if (!orgName.value || !orgSlug.value || !fullName.value || !email.value || !password.value) {
    error.value = 'Fill in every field to create your organisation.';
    return;
  }
  if (password.value.length < 8) {
    error.value = 'Choose a password of at least 8 characters for your account security.';
    return;
  }
  submitting.value = true;
  try {
    await auth.register({
      orgName: orgName.value.trim(),
      orgSlug: orgSlug.value,
      billingEmail: email.value.trim(),
      email: email.value.trim(),
      name: fullName.value.trim(),
      password: password.value,
      planSlug: plan.value,
    });
    router.push('/onboarding');
  } catch (e) {
    error.value = toUserFacingError(e).message;
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <div class="flex min-h-full items-center justify-center bg-slate-50 px-4 py-12">
    <div class="w-full max-w-lg">
      <div class="mb-8 flex flex-col items-center">
        <div class="flex h-12 w-12 items-center justify-center rounded-xl bg-primary-600 text-xl font-bold text-white">
          F
        </div>
        <h1 class="mt-4 text-xl font-semibold text-slate-900">
          Start your 14-day free trial
        </h1>
        <p class="mt-1 text-sm text-slate-500">
          Spin up your {{ APP_NAME }} organisation in under a minute.
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

        <fieldset class="mb-5">
          <legend class="mb-2 text-sm font-medium text-slate-700">
            Choose a plan
          </legend>
          <div class="grid grid-cols-3 gap-2">
            <button
              v-for="p in PLANS"
              :key="p.slug"
              type="button"
              :class="[
                'rounded-lg border p-3 text-left transition-colors',
                plan === p.slug ? 'border-primary-500 ring-2 ring-primary-200' : 'border-slate-200 hover:border-slate-300',
              ]"
              @click="plan = p.slug"
            >
              <p class="text-sm font-semibold text-slate-900">
                {{ p.name }}
              </p>
              <p class="text-xs font-medium text-primary-600">
                {{ p.price }}
              </p>
              <p class="mt-1 text-[11px] leading-tight text-slate-400">
                {{ p.blurb }}
              </p>
            </button>
          </div>
        </fieldset>

        <div class="space-y-4">
          <FmInput
            :model-value="orgName"
            label="Organisation name"
            placeholder="Acme Inc."
            help="Your company or team name."
            @update:model-value="onOrgNameInput"
          />
          <FmInput
            v-model="orgSlug"
            label="Organisation URL"
            prefix="flowamaz.io/"
            hint="Auto-generated from the name. Lowercase letters, numbers and dashes."
            @update:model-value="slugTouched = true"
          />
          <FmInput
            v-model="fullName"
            label="Full name"
            placeholder="Jane Doe"
            autocomplete="name"
          />
          <FmInput
            v-model="email"
            type="email"
            label="Email"
            placeholder="jane@acme.com"
            autocomplete="email"
          />
          <div>
            <FmInput
              v-model="password"
              type="password"
              label="Password"
              placeholder="At least 8 characters"
              autocomplete="new-password"
            />
            <div
              v-if="password.length > 0"
              class="mt-2 flex items-center gap-2"
            >
              <div class="h-1.5 flex-1 overflow-hidden rounded-full bg-slate-200">
                <div
                  :class="['h-full rounded-full transition-all', strengthColor]"
                  :style="{ width: `${(strength.score / 4) * 100}%` }"
                />
              </div>
              <span class="text-xs text-slate-500">{{ strength.label }}</span>
            </div>
          </div>
        </div>

        <FmButton
          type="submit"
          :loading="submitting"
          block
          class="mt-6"
        >
          Create organisation
        </FmButton>
      </form>

      <p class="mt-6 text-center text-sm text-slate-500">
        Already have an account?
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
