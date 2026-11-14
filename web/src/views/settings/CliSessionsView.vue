<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { Terminal } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import FmModal from '@/components/common/FmModal.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import FmTooltip from '@/components/common/FmTooltip.vue';
import { useToast } from '@/composables/useToast';
import { cliAuthService, type CliSession } from '@/services/cliAuth.service';
import { toUserFacingError } from '@/utils/error.util';
import { formatDate, fromNow } from '@/utils/date.util';

// Settings → CLI sessions: every terminal signed in to Flowamaz as you (`fmz auth login`). Ending one cuts that
// terminal off straight away; it has to run `fmz auth login` again. Only your own sessions are shown.
const toast = useToast();

const loading = ref(true);
const loadError = ref('');
const sessions = ref<CliSession[]>([]);

const revokeTarget = ref<CliSession | null>(null);
const revoking = ref(false);
const docsUrl = 'https://docs.flowamaz.io';

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = '';
  try {
    sessions.value = await cliAuthService.listSessions();
  } catch (e) {
    loadError.value = toUserFacingError(e).message;
  } finally {
    loading.value = false;
  }
}

onMounted(load);

function openDocs(): void {
  window.open(docsUrl, '_blank', 'noopener');
}

async function confirmRevoke(): Promise<void> {
  const target = revokeTarget.value;
  if (!target) return;
  revoking.value = true;
  try {
    await cliAuthService.revokeSession(target.id);
    sessions.value = sessions.value.filter((s) => s.id !== target.id);
    toast.success(`Signed out "${target.name}". That terminal needs fmz auth login to sign in again.`);
    revokeTarget.value = null;
  } catch (e) {
    toast.error(toUserFacingError(e).message);
    revokeTarget.value = null;
    await load();
  } finally {
    revoking.value = false;
  }
}
</script>

<template>
  <div class="mx-auto max-w-4xl space-y-6 p-6">
    <header class="flex items-center justify-between gap-4">
      <div>
        <h1 class="flex items-center gap-2 text-2xl font-semibold text-slate-900">
          CLI sessions
          <FmTooltip
            text="Each terminal where you ran fmz auth login and approved it in this browser."
            label="About CLI sessions"
          />
        </h1>
        <p class="text-sm text-slate-500">
          Terminals signed in to Flowamaz as you. A session stays signed in while it's used and ends after 30 days
          without use. Sign out any you don't recognise.
        </p>
      </div>
      <FmButton
        variant="secondary"
        :disabled="loading"
        data-testid="cli-sessions-refresh"
        @click="load"
      >
        Refresh
      </FmButton>
    </header>

    <div
      v-if="loading"
      class="flex justify-center py-16 text-primary-600"
      data-testid="cli-sessions-loading"
    >
      <FmSpinner size="lg" />
    </div>

    <FmErrorState
      v-else-if="loadError"
      title="Couldn't load your CLI sessions"
      :description="loadError"
      action-label="Try again"
      help-article="settings/cli-sessions"
      @action="load"
    />

    <div
      v-else-if="sessions.length === 0"
      data-testid="cli-sessions-empty"
    >
      <FmEmptyState
        :icon="Terminal"
        title="No terminals are signed in"
        description="Install the CLI (npm install -g @flowamaz/cli), run fmz auth login, and approve the code here in your browser. The terminal will show up in this list, and you can sign it out from here at any time."
        cta-label="Read the CLI guide"
        @cta="openDocs"
      />
    </div>

    <div
      v-else
      class="overflow-hidden rounded-xl border border-slate-200 bg-white"
    >
      <table
        class="min-w-full divide-y divide-slate-200 text-sm"
        data-testid="cli-sessions-table"
      >
        <thead class="bg-slate-50 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
          <tr>
            <th class="px-4 py-3">
              Terminal
            </th>
            <th class="px-4 py-3">
              Signed in
            </th>
            <th class="px-4 py-3">
              Last used
            </th>
            <th class="px-4 py-3">
              Ends if unused
            </th>
            <th class="px-4 py-3 text-right">
              Actions
            </th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100">
          <tr
            v-for="s in sessions"
            :key="s.id"
            data-testid="cli-session-row"
          >
            <td class="px-4 py-3">
              <div class="flex items-center gap-2 font-medium text-slate-800">
                <Terminal class="h-4 w-4 text-slate-400" />
                {{ s.name }}
                <FmBadge
                  v-if="s.isCurrent"
                  variant="slate"
                >
                  This CLI
                </FmBadge>
              </div>
              <p
                v-if="s.lastUsedIp"
                class="mt-0.5 text-xs text-slate-500"
              >
                Last seen from {{ s.lastUsedIp }}
              </p>
            </td>
            <td class="px-4 py-3 text-slate-500">
              {{ formatDate(s.createdAt) }}
            </td>
            <td class="px-4 py-3 text-slate-500">
              {{ fromNow(s.lastUsedAt) }}
            </td>
            <td class="px-4 py-3 text-slate-500">
              {{ formatDate(s.expiresAt) }}
            </td>
            <td class="px-4 py-3 text-right">
              <button
                type="button"
                class="text-sm font-medium text-danger-600 hover:text-danger-700"
                :data-testid="`cli-session-revoke-${s.id}`"
                @click="revokeTarget = s"
              >
                Sign out
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <FmModal
      :model-value="revokeTarget !== null"
      title="Sign out this terminal?"
      size="sm"
      @update:model-value="revokeTarget = null"
    >
      <p class="text-sm text-slate-600">
        <strong>{{ revokeTarget?.name }}</strong> will be signed out straight away. Any <code>fmz</code> command running there will
        fail, and it will need <code>fmz auth login</code> to sign in again.
      </p>
      <template #footer>
        <FmButton
          variant="secondary"
          @click="revokeTarget = null"
        >
          Cancel
        </FmButton>
        <FmButton
          variant="danger"
          :loading="revoking"
          data-testid="cli-session-revoke-confirm"
          @click="confirmRevoke"
        >
          Sign out
        </FmButton>
      </template>
    </FmModal>
  </div>
</template>
