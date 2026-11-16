<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue';
import { Activity } from 'lucide-vue-next';
import FmEmptyState from './FmEmptyState.vue';
import FmErrorState from './FmErrorState.vue';
import FmSkeleton from './FmSkeleton.vue';
import RunTable from './RunTable.vue';
import { describeError } from '../api';
import { useWorkbenchApi } from '../host';
import { isRunEnded, RUN_STATUSES } from '../runs';
import type { InstanceListItem } from '../types';

// The runs page on the WorkbenchApi alone: status filter, production / test tabs, and polling while runs are going.
const props = withDefaults(defineProps<{ pollMs?: number }>(), { pollMs: 5000 });
const emit = defineEmits<{ open: [runId: string]; 'go-to-workflows': [] }>();

const api = useWorkbenchApi();
const runs = ref<InstanceListItem[]>([]);
const names = ref(new Map<string, string>());
const loading = ref(true);
const error = ref<{ message: string; code?: string; status?: number } | null>(null);
const tab = ref<'production' | 'test'>('production');
const status = ref('');
let timer: ReturnType<typeof setTimeout> | null = null;

const shown = computed(() => runs.value.filter((r) => (tab.value === 'test' ? r.isTest : !r.isTest)));

async function load(): Promise<void> {
  try {
    const [page, workflows] = await Promise.all([
      api.listRuns({ status: status.value || undefined, includeTest: tab.value === 'test', pageSize: 100 }),
      names.value.size === 0 ? api.listWorkflows(1, 100) : Promise.resolve(null),
    ]);
    runs.value = page.items;
    if (workflows) names.value = new Map(workflows.items.map((w) => [w.id, w.name]));
    error.value = null;
  } catch (e) {
    error.value = describeError(e);
  } finally {
    loading.value = false;
  }
  schedule();
}

function schedule(): void {
  if (timer) clearTimeout(timer);
  timer = runs.value.some((r) => !isRunEnded(r.status)) ? setTimeout(() => void load(), props.pollMs) : null;
}

function reload(): void {
  loading.value = true;
  void load();
}

function switchTab(next: 'production' | 'test'): void {
  tab.value = next;
  reload();
}

onMounted(load);
onUnmounted(() => { if (timer) clearTimeout(timer); });

defineExpose({ reload });
</script>

<template>
  <div class="space-y-6">
    <div class="flex gap-1 border-b border-slate-200">
      <button
        v-for="t in (['production', 'test'] as const)"
        :key="t"
        type="button"
        :class="[
          '-mb-px border-b-2 px-4 py-2 text-sm font-medium capitalize',
          tab === t
            ? (t === 'test' ? 'border-amber-500 text-amber-700' : 'border-primary-600 text-primary-700')
            : 'border-transparent text-slate-500 hover:text-slate-700',
        ]"
        @click="switchTab(t)"
      >
        {{ t }}
      </button>
    </div>

    <select
      v-model="status"
      aria-label="Status"
      class="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
      @change="reload"
    >
      <option value="">
        All statuses
      </option>
      <option
        v-for="s in RUN_STATUSES"
        :key="s"
        :value="s"
      >
        {{ s }}
      </option>
    </select>

    <div
      v-if="loading"
      class="space-y-3"
    >
      <FmSkeleton
        :count="6"
        height="h-14"
        rounded="lg"
      />
    </div>
    <FmErrorState
      v-else-if="error"
      title="Couldn't load runs"
      :description="error.message"
      :status-code="error.status"
      :error-code="error.code"
      action-label="Try again"
      @action="reload"
    />
    <FmEmptyState
      v-else-if="shown.length === 0"
      :icon="Activity"
      :title="tab === 'test' ? 'No test runs yet' : 'No runs yet'"
      :description="tab === 'test'
        ? 'Start a test run of any workflow to try it without a production run.'
        : 'Start a run of a published workflow, or send its webhook, and it appears here.'"
      cta-label="Go to Workflows"
      @cta="emit('go-to-workflows')"
    />
    <RunTable
      v-else
      :runs="shown"
      :workflow-name="(id: string) => names.get(id) ?? 'Deleted workflow'"
      :show-expiry="tab === 'test'"
      @open="(id: string) => emit('open', id)"
    />
  </div>
</template>
