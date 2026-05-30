<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import { instanceService } from '@/services/instance.service';
import type { InstanceStatus, VariableResponse } from '@/types';

const props = defineProps<{
  workspaceId: string;
  instanceId: string;
  status: InstanceStatus;
}>();

const TERMINAL: InstanceStatus[] = ['Completed', 'Failed', 'Cancelled'];
const POLL_MS = 5000;

const variables = ref<VariableResponse[]>([]);
const loading = ref(true);
let timer: ReturnType<typeof setInterval> | null = null;

function isRunning(): boolean {
  return !TERMINAL.includes(props.status);
}

async function load(): Promise<void> {
  try {
    variables.value = await instanceService.variables(props.workspaceId, props.instanceId);
  } finally {
    loading.value = false;
  }
}

function startPolling(): void {
  stopPolling();
  if (isRunning()) {
    timer = setInterval(() => void load(), POLL_MS);
  }
}

function stopPolling(): void {
  if (timer !== null) {
    clearInterval(timer);
    timer = null;
  }
}

// On running instances poll; on terminal instances a single static snapshot is enough.
watch(
  () => props.status,
  () => {
    if (isRunning()) startPolling();
    else stopPolling();
  },
);

onMounted(async () => {
  await load();
  startPolling();
});

onBeforeUnmount(stopPolling);
</script>

<template>
  <div class="overflow-hidden rounded-xl border border-slate-200 bg-white">
    <div class="flex items-center justify-between border-b border-slate-200 px-4 py-2">
      <span class="text-xs font-semibold uppercase tracking-wide text-slate-500">Variable context</span>
      <span
        v-if="isRunning()"
        class="text-xs text-teal-600"
      >● Live · refreshes every 5s</span>
      <span
        v-else
        class="text-xs text-slate-400"
      >Final snapshot</span>
    </div>

    <table
      v-if="variables.length > 0"
      class="min-w-full divide-y divide-slate-200 text-sm"
    >
      <tbody class="divide-y divide-slate-100">
        <tr
          v-for="v in variables"
          :key="v.name"
        >
          <td class="w-1/3 px-4 py-3 font-medium text-slate-700">
            {{ v.name }}
          </td>
          <td class="px-4 py-3 font-mono text-xs text-slate-600">
            <span
              v-if="v.isSensitive"
              class="rounded bg-slate-100 px-1.5 py-0.5 text-slate-400"
            >sensitive — hidden</span>
            <span v-else>{{ v.value }}</span>
          </td>
        </tr>
      </tbody>
    </table>

    <FmEmptyState
      v-else-if="!loading"
      class="py-8"
      title="No variables yet"
      description="Variables appear here as nodes execute and produce outputs. Trigger or replay this workflow to populate the context."
    />

    <div
      v-else
      class="px-4 py-8 text-center text-sm text-slate-400"
    >
      Loading variables…
    </div>
  </div>
</template>
