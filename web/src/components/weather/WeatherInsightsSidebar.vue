<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { analyticsService } from '../../services/analytics.service';
import type { InsightResponse } from '../../types/workflow';

const props = defineProps<{ workspaceId: string; open: boolean }>();
const emit = defineEmits<{ close: [] }>();

const insights = ref<InsightResponse[]>([]);
const loading = ref(false);
const error = ref<string | null>(null);

async function load() {
  loading.value = true;
  error.value = null;
  try {
    const result = await analyticsService.insights(props.workspaceId, { acknowledged: false });
    insights.value = result.data ?? [];
  } catch (e: unknown) {
    error.value = `Failed to load insights. ${e instanceof Error ? e.message : 'Please try again.'}`;
  } finally {
    loading.value = false;
  }
}

async function acknowledge(id: string) {
  await analyticsService.acknowledge(props.workspaceId, id);
  insights.value = insights.value.filter(i => i.id !== id);
}

onMounted(() => { if (props.open) load(); });

const SEVERITY_COLOURS: Record<string, string> = {
  Info: 'bg-blue-100 text-blue-800',
  Warning: 'bg-amber-100 text-amber-800',
  Critical: 'bg-red-100 text-red-800',
};

function groupByWorkflow(items: InsightResponse[]) {
  const map = new Map<string, InsightResponse[]>();
  for (const item of items) {
    const key = item.workflowDefinitionId;
    if (!map.has(key)) map.set(key, []);
    map.get(key)!.push(item);
  }
  return map;
}
</script>

<template>
  <Transition name="slide-right">
    <div v-if="open" class="fixed inset-y-0 right-0 z-50 w-96 bg-white shadow-2xl border-l border-neutral-200 flex flex-col">
      <!-- Header -->
      <div class="flex items-center justify-between px-5 py-4 border-b border-neutral-200">
        <h2 class="font-semibold text-neutral-800">Insights</h2>
        <button class="text-neutral-400 hover:text-neutral-700" @click="$emit('close')">✕</button>
      </div>

      <!-- Body -->
      <div class="flex-1 overflow-y-auto p-4 space-y-4">
        <div v-if="loading" class="text-sm text-neutral-400 text-center py-8">Loading…</div>
        <div v-else-if="error" class="text-sm text-red-600 bg-red-50 rounded-xl p-3">{{ error }}</div>
        <div v-else-if="!insights.length" class="text-sm text-neutral-400 text-center py-8">
          No unacknowledged insights. All workflows are healthy.
        </div>
        <template v-else>
          <div v-for="[wfId, items] in groupByWorkflow(insights)" :key="wfId" class="space-y-2">
            <p class="text-xs font-medium text-neutral-500 uppercase tracking-wide truncate">{{ items[0].workflowDefinitionId }}</p>
            <div v-for="item in items" :key="item.id" class="bg-neutral-50 rounded-xl p-3 space-y-2">
              <div class="flex items-start justify-between gap-2">
                <span :class="['text-xs font-medium rounded-full px-2 py-0.5', SEVERITY_COLOURS[item.severity] ?? 'bg-neutral-100 text-neutral-700']">
                  {{ item.severity }}
                </span>
                <button
                  class="text-xs text-neutral-400 hover:text-neutral-700 shrink-0"
                  @click="acknowledge(item.id)"
                >Acknowledge</button>
              </div>
              <p class="text-sm text-neutral-700">{{ item.message }}</p>
            </div>
          </div>
        </template>
      </div>
    </div>
  </Transition>
</template>
