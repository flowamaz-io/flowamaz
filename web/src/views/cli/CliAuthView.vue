<script setup lang="ts">
import { computed, ref } from 'vue';
import { useRoute } from 'vue-router';
import { CheckCircle2, Terminal } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import { useAuthStore } from '@/stores/auth.store';
import { cliAuthService, normaliseUserCode } from '@/services/cliAuth.service';
import { toUserFacingError } from '@/utils/error.util';

// `fmz auth login` sends people here (CliAuthService: {app}/cli-auth?code=XXXX-XXXX). Approving binds this
// browser's sign-in to the CLI, so the terminal acts as this user.
const route = useRoute();
const auth = useAuthStore();

const code = ref(typeof route.query.code === 'string' ? normaliseUserCode(route.query.code) : '');
const status = ref<'idle' | 'approving' | 'approved'>('idle');
const error = ref<string | null>(null);
const valid = computed(() => /^[A-Z0-9]{4}-[A-Z0-9]{4}$/.test(normaliseUserCode(code.value)));
const who = computed(() => auth.user?.email ?? 'your account');

async function approve(): Promise<void> {
  if (!valid.value) {
    error.value = 'Enter the 8-character code your terminal shows, e.g. ABCD-2345.';
    return;
  }
  status.value = 'approving';
  error.value = null;
  try {
    await cliAuthService.approve(normaliseUserCode(code.value));
    status.value = 'approved';
  } catch (e) {
    status.value = 'idle';
    error.value = toUserFacingError(e).message;
  }
}
</script>

<template>
  <div class="flex min-h-[60vh] items-center justify-center px-4 py-12">
    <div
      class="w-full max-w-md rounded-xl border border-slate-200 bg-white p-6 shadow-sm"
      data-testid="cli-auth"
    >
      <template v-if="status === 'approved'">
        <div class="flex flex-col items-center gap-3 py-4 text-center">
          <CheckCircle2 class="h-12 w-12 text-emerald-600" />
          <h1 class="text-lg font-semibold text-slate-900">
            CLI signed in
          </h1>
          <p class="text-sm text-slate-600">
            Your terminal is now signed in as {{ who }}. Go back to it; you can close this tab.
          </p>
        </div>
      </template>
      <template v-else>
        <div class="flex items-center gap-2">
          <Terminal class="h-5 w-5 text-indigo-600" />
          <h1 class="text-lg font-semibold text-slate-900">
            Sign in the Flowamaz CLI
          </h1>
        </div>
        <p class="mt-2 text-sm text-slate-600">
          Check that this code matches the one in your terminal, then approve. The CLI will act as
          <span class="font-medium">{{ who }}</span>. If you didn't run <code>fmz auth login</code>, close this tab.
        </p>
        <form
          class="mt-4 space-y-4"
          @submit.prevent="approve"
        >
          <label class="block">
            <span class="text-sm font-medium text-slate-700">Code</span>
            <input
              v-model="code"
              type="text"
              autocomplete="off"
              spellcheck="false"
              placeholder="ABCD-2345"
              class="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-center font-mono text-lg tracking-widest text-slate-900 uppercase focus:border-indigo-400 focus:outline-none focus:ring-2 focus:ring-indigo-100"
              data-testid="cli-auth-code"
              @input="error = null"
            >
          </label>
          <FmAlert
            v-if="error"
            type="error"
          >
            {{ error }}
          </FmAlert>
          <FmButton
            class="w-full"
            :loading="status === 'approving'"
            data-testid="cli-auth-approve"
            @click="approve"
          >
            Approve sign-in
          </FmButton>
        </form>
      </template>
    </div>
  </div>
</template>
