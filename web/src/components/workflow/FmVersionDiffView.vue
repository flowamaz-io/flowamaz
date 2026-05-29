<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { X } from 'lucide-vue-next';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import { workflowService } from '@/services/workflow.service';
import { toUserFacingError } from '@/utils/error.util';
import type { WorkflowDiff, WorkflowDiffLine } from '@/types';

const props = defineProps<{
  modelValue: boolean;
  workspaceId: string;
  workflowId: string;
  fromSha: string;
  toSha: string;
}>();

const emit = defineEmits<{ 'update:modelValue': [value: boolean] }>();

const loading = ref(false);
const error = ref<string | null>(null);
const diff = ref<WorkflowDiff | null>(null);

function close(): void {
  emit('update:modelValue', false);
}

// Left pane = "from" (context + removed); right pane = "to" (context + added).
const leftLines = computed<WorkflowDiffLine[]>(() => (diff.value?.lines ?? []).filter((l) => l.type !== 'added'));
const rightLines = computed<WorkflowDiffLine[]>(() => (diff.value?.lines ?? []).filter((l) => l.type !== 'removed'));

const hasChanges = computed(() => (diff.value?.lines.length ?? 0) > 0);

const summaryText = computed(() => {
  const s = diff.value?.summary;
  if (!s) return '';
  const parts: string[] = [];
  if (s.addedNodes.length) parts.push(`${s.addedNodes.length} node${s.addedNodes.length !== 1 ? 's' : ''} added`);
  if (s.removedNodes.length) parts.push(`${s.removedNodes.length} node${s.removedNodes.length !== 1 ? 's' : ''} removed`);
  if (s.modifiedNodes.length) parts.push(`${s.modifiedNodes.length} node${s.modifiedNodes.length !== 1 ? 's' : ''} modified`);
  return parts.length ? parts.join(', ') : 'No node-level changes';
});

function lineClass(type: WorkflowDiffLine['type']): string {
  if (type === 'added') return 'bg-green-50 text-green-800';
  if (type === 'removed') return 'bg-red-50 text-red-800';
  return 'text-slate-700';
}

async function load(): Promise<void> {
  loading.value = true;
  error.value = null;
  diff.value = null;
  try {
    diff.value = await workflowService.diff(props.workspaceId, props.workflowId, props.fromSha, props.toSha);
  } catch (err) {
    error.value = toUserFacingError(err).message;
  } finally {
    loading.value = false;
  }
}

watch(
  () => props.modelValue,
  (open) => {
    if (open && props.fromSha && props.toSha) void load();
  },
);
</script>

<template>
  <Teleport to="body">
    <div
      v-if="modelValue"
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
      @click.self="close"
    >
      <div class="flex h-[85vh] w-full max-w-6xl flex-col overflow-hidden rounded-xl bg-white shadow-xl">
        <div class="flex items-center justify-between border-b border-slate-200 px-5 py-3">
          <div>
            <h2 class="text-sm font-semibold text-slate-900">
              Compare versions
            </h2>
            <p class="mt-0.5 font-mono text-xs text-slate-400">
              {{ fromSha.slice(0, 7) }} → {{ toSha.slice(0, 7) }}
            </p>
          </div>
          <button
            type="button"
            class="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600"
            aria-label="Close"
            @click="close"
          >
            <X class="h-5 w-5" />
          </button>
        </div>

        <div
          v-if="diff && hasChanges"
          class="flex flex-wrap items-center gap-2 border-b border-slate-200 bg-slate-50 px-5 py-2 text-xs"
        >
          <span class="font-medium text-slate-600">{{ summaryText }}</span>
          <span
            v-for="n in diff.summary.addedNodes"
            :key="`a-${n}`"
            class="rounded bg-green-100 px-1.5 py-0.5 font-mono text-green-700"
          >+ {{ n }}</span>
          <span
            v-for="n in diff.summary.removedNodes"
            :key="`r-${n}`"
            class="rounded bg-red-100 px-1.5 py-0.5 font-mono text-red-700"
          >− {{ n }}</span>
          <span
            v-for="n in diff.summary.modifiedNodes"
            :key="`m-${n}`"
            class="rounded bg-amber-100 px-1.5 py-0.5 font-mono text-amber-700"
          >~ {{ n }}</span>
        </div>

        <div class="flex-1 overflow-hidden">
          <div
            v-if="loading"
            class="flex h-full items-center justify-center"
          >
            <FmSpinner size="lg" />
          </div>

          <FmErrorState
            v-else-if="error"
            title="Couldn't load the diff"
            :description="error"
            action-label="Retry"
            @action="load"
          />

          <div
            v-else-if="diff && !hasChanges"
            class="flex h-full flex-col items-center justify-center gap-2 px-6 text-center"
          >
            <p class="text-sm font-medium text-slate-700">
              These two versions are identical
            </p>
            <p class="text-xs text-slate-400">
              Nothing changed in the workflow YAML between these commits. Pick a different pair to compare.
            </p>
          </div>

          <div
            v-else-if="diff"
            class="grid h-full grid-cols-2 divide-x divide-slate-200 overflow-auto"
          >
            <div class="overflow-auto">
              <div class="sticky top-0 bg-slate-100 px-3 py-1.5 text-xs font-semibold uppercase tracking-wider text-slate-500">
                From · {{ fromSha.slice(0, 7) }}
              </div>
              <pre class="text-xs leading-5"><div
                v-for="(line, i) in leftLines"
                :key="`l-${i}`"
                :class="['flex px-3', lineClass(line.type)]"
              ><span class="mr-3 w-8 shrink-0 select-none text-right text-slate-300">{{ line.oldLineNumber ?? '' }}</span><span class="whitespace-pre-wrap break-all">{{ line.content }}</span></div></pre>
            </div>

            <div class="overflow-auto">
              <div class="sticky top-0 bg-slate-100 px-3 py-1.5 text-xs font-semibold uppercase tracking-wider text-slate-500">
                To · {{ toSha.slice(0, 7) }}
              </div>
              <pre class="text-xs leading-5"><div
                v-for="(line, i) in rightLines"
                :key="`r-${i}`"
                :class="['flex px-3', lineClass(line.type)]"
              ><span class="mr-3 w-8 shrink-0 select-none text-right text-slate-300">{{ line.newLineNumber ?? '' }}</span><span class="whitespace-pre-wrap break-all">{{ line.content }}</span></div></pre>
            </div>
          </div>
        </div>
      </div>
    </div>
  </Teleport>
</template>
