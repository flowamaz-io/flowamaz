<template>
  <Transition
    enter-active-class="transition-transform duration-200"
    enter-from-class="translate-x-full"
    leave-active-class="transition-transform duration-200"
    leave-to-class="translate-x-full"
  >
    <div
      v-if="node"
      class="w-72 bg-gray-900 border-l border-gray-700 flex flex-col shrink-0 overflow-hidden"
    >
      <!-- Header -->
      <div class="flex items-center gap-2 px-4 py-3 border-b border-gray-700">
        <span
          class="px-2 py-0.5 rounded text-xs font-semibold text-white"
          :style="{ backgroundColor: nodeColor }"
        >{{ node.type }}</span>
        <span class="text-xs text-gray-400 font-mono flex-1 truncate">{{ node.id }}</span>
        <button
          class="p-1 rounded hover:bg-gray-700 text-gray-400 hover:text-indigo-400 transition-colors"
          title="Help with this node type"
          @click="$emit('help', node.type)"
        >
          <HelpCircle class="w-4 h-4" />
        </button>
        <button
          class="p-1 rounded hover:bg-gray-700 text-gray-400 hover:text-white transition-colors"
          @click="$emit('close')"
        >
          <X class="w-4 h-4" />
        </button>
      </div>

      <!-- Tabs -->
      <div class="flex border-b border-gray-700 text-xs">
        <button
          v-for="tab in tabs"
          :key="tab"
          :class="['flex-1 py-2 font-medium transition-colors', activeTab === tab ? 'text-indigo-400 border-b-2 border-indigo-400' : 'text-gray-500 hover:text-gray-300']"
          @click="activeTab = tab"
        >
          {{ tab }}
        </button>
      </div>

      <!-- Tab content -->
      <div class="flex-1 overflow-y-auto p-4 space-y-4 text-sm">
        <template v-if="activeTab === 'Config'">
          <!-- Label -->
          <div class="space-y-1">
            <label class="text-xs text-gray-400 font-medium">Label</label>
            <input
              :value="node.label"
              class="w-full bg-gray-800 border border-gray-600 rounded px-3 py-1.5 text-sm text-gray-100 outline-none focus:border-indigo-500 transition-colors"
              @input="$emit('update', { label: ($event.target as HTMLInputElement).value })"
            >
          </div>

          <!-- Annotation content -->
          <template v-if="node.type === 'annotation'">
            <div class="space-y-1">
              <label class="text-xs text-gray-400 font-medium">Note content</label>
              <textarea
                :value="node.content"
                rows="4"
                class="w-full bg-gray-800 border border-gray-600 rounded px-3 py-1.5 text-sm text-gray-100 outline-none focus:border-indigo-500 resize-none transition-colors"
                @input="$emit('update', { content: ($event.target as HTMLTextAreaElement).value })"
              />
            </div>
          </template>

          <!-- Generic config JSON -->
          <div
            v-if="!['annotation', 'end', 'trigger'].includes(node.type)"
            class="space-y-1"
          >
            <label class="text-xs text-gray-400 font-medium">Config (JSON)</label>
            <textarea
              :value="configJson"
              rows="6"
              class="w-full bg-gray-800 border border-gray-600 rounded px-3 py-1.5 text-xs font-mono text-gray-100 outline-none focus:border-indigo-500 resize-none transition-colors"
              @change="onConfigChange(($event.target as HTMLTextAreaElement).value)"
            />
          </div>
        </template>

        <template v-else-if="activeTab === 'Retry'">
          <div class="space-y-3">
            <div class="space-y-1">
              <label class="text-xs text-gray-400 font-medium">Max attempts</label>
              <input
                type="number"
                min="1"
                max="100"
                :value="node.retry?.maxAttempts ?? 3"
                class="w-full bg-gray-800 border border-gray-600 rounded px-3 py-1.5 text-sm text-gray-100 outline-none focus:border-indigo-500 transition-colors"
                @change="$emit('update', { retry: { maxAttempts: +($event.target as HTMLInputElement).value, backoffSeconds: node.retry?.backoffSeconds ?? 5, backoffMultiplier: node.retry?.backoffMultiplier ?? 2 } })"
              >
            </div>
            <div class="space-y-1">
              <label class="text-xs text-gray-400 font-medium">Backoff (seconds)</label>
              <input
                type="number"
                min="0"
                :value="node.retry?.backoffSeconds ?? 5"
                class="w-full bg-gray-800 border border-gray-600 rounded px-3 py-1.5 text-sm text-gray-100 outline-none focus:border-indigo-500 transition-colors"
                @change="$emit('update', { retry: { maxAttempts: node.retry?.maxAttempts ?? 3, backoffSeconds: +($event.target as HTMLInputElement).value, backoffMultiplier: node.retry?.backoffMultiplier ?? 2 } })"
              >
            </div>
          </div>
        </template>

        <template v-else-if="activeTab === 'Timeout'">
          <div class="space-y-3">
            <div class="space-y-1">
              <label class="text-xs text-gray-400 font-medium">Timeout (seconds)</label>
              <input
                type="number"
                min="1"
                :value="node.timeout?.seconds ?? 30"
                class="w-full bg-gray-800 border border-gray-600 rounded px-3 py-1.5 text-sm text-gray-100 outline-none focus:border-indigo-500 transition-colors"
                @change="$emit('update', { timeout: { ...node.timeout, seconds: +($event.target as HTMLInputElement).value } })"
              >
            </div>
            <div class="space-y-1">
              <label class="text-xs text-gray-400 font-medium">On timeout (node ID)</label>
              <input
                :value="node.timeout?.onTimeout ?? ''"
                class="w-full bg-gray-800 border border-gray-600 rounded px-3 py-1.5 text-sm text-gray-100 font-mono outline-none focus:border-indigo-500 transition-colors"
                placeholder="Leave empty to fail"
                @input="$emit('update', { timeout: { seconds: node.timeout?.seconds ?? 30, onTimeout: ($event.target as HTMLInputElement).value || undefined } })"
              >
            </div>
          </div>
        </template>

        <template v-else-if="activeTab === 'Compensation'">
          <div class="space-y-3">
            <div class="space-y-1">
              <label class="text-xs text-gray-400 font-medium">Strategy</label>
              <select
                :value="node.compensate?.strategy ?? 'backward'"
                class="w-full bg-gray-800 border border-gray-600 rounded px-3 py-1.5 text-sm text-gray-100 outline-none focus:border-indigo-500 transition-colors"
                @change="$emit('update', { compensate: { ...node.compensate, strategy: ($event.target as HTMLSelectElement).value as 'backward' | 'forward' | 'pivot' } })"
              >
                <option value="backward">
                  Backward (undo all)
                </option>
                <option value="forward">
                  Forward (retry failed)
                </option>
                <option value="pivot">
                  Pivot (backward then forward)
                </option>
              </select>
            </div>
          </div>
        </template>
      </div>
    </div>
  </Transition>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';
import { HelpCircle, X } from 'lucide-vue-next';
import type { CanvasNode } from '@/types/canvas.types';
import { NODE_VISUALS } from '@/types/canvas.types';

const props = defineProps<{ node: CanvasNode | null }>();
const emit = defineEmits<{
  close: [];
  update: [updates: Partial<CanvasNode>];
  help: [nodeType: string];
}>();

const tabs = ['Config', 'Retry', 'Timeout', 'Compensation'] as const;
const activeTab = ref<(typeof tabs)[number]>('Config');

const nodeColor = computed(() =>
  props.node ? NODE_VISUALS[props.node.type]?.color ?? '#374151' : '#374151'
);

const configJson = computed(() =>
  props.node ? JSON.stringify(props.node.config, null, 2) : '{}'
);

function onConfigChange(value: string) {
  try {
    const parsed = JSON.parse(value);
    emit('update', { config: parsed });
  } catch { /* ignore invalid JSON */ }
}
</script>
