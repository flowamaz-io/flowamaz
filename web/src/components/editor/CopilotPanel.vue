<script setup lang="ts">
import { ref, computed } from 'vue';
import FmTooltip from '@/components/common/FmTooltip.vue';

const props = defineProps<{
  open: boolean;
  history: string[];
}>();

const emit = defineEmits<{
  close: [];
  command: [cmd: string];
}>();

const command = ref('');
const loading = ref(false);
const lastResult = ref<{ patch: string | null; pattern: string | null; error: string | null } | null>(null);
const showDiff = ref(false);

const suggestions = computed(() =>
  props.history.filter(h => h.toLowerCase().includes(command.value.toLowerCase()) && h !== command.value).slice(0, 5),
);

async function submit() {
  const cmd = command.value.trim();
  if (!cmd || loading.value) return;
  loading.value = true;
  lastResult.value = null;
  showDiff.value = false;
  emit('command', cmd);
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') emit('close');
  if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); submit(); }
}

// Called by parent with result
function setResult(patch: string | null, pattern: string | null, error: string | null = null) {
  lastResult.value = { patch, pattern, error };
  loading.value = false;
  if (patch) showDiff.value = true;
  command.value = '';
}

function applyAndClose() {
  if (lastResult.value?.patch) emit('command', `__apply__${lastResult.value.patch}`);
  emit('close');
}

defineExpose({ setResult });
</script>

<template>
  <Transition name="slide-up">
    <div
      v-if="open"
      class="fixed bottom-0 left-0 right-0 z-50 bg-white border-t border-gray-200 shadow-2xl"
      style="height: 42vh;"
    >
      <!-- Header -->
      <div class="flex items-center justify-between px-5 py-3 border-b border-gray-200 bg-gray-50">
        <div class="flex items-center gap-2">
          <span class="text-teal-600 font-bold text-sm">✦ Co-pilot</span>
          <kbd class="text-xs bg-gray-100 text-gray-600 rounded px-1 py-0.5">Ctrl+K</kbd>
          <FmTooltip
            text="Ask Co-pilot to modify your workflow in plain English. Ctrl+K"
            label="About Co-pilot"
          />
        </div>
        <button
          class="text-gray-400 hover:text-gray-700 text-sm"
          @click="$emit('close')"
        >
          ✕
        </button>
      </div>

      <div class="flex flex-col h-[calc(100%-3rem)] p-4 gap-3 overflow-hidden">
        <!-- Command input -->
        <div class="relative">
          <input
            v-model="command"
            type="text"
            placeholder="What would you like to do? e.g. 'Add a 24h timeout to manager-gate'"
            class="w-full bg-white text-gray-900 text-sm rounded-xl px-4 py-3 border border-gray-300 focus:border-teal-500 focus:outline-none placeholder-gray-400"
            :disabled="loading"
            autofocus
            @keydown="onKeydown"
          >
          <button
            class="absolute right-3 top-1/2 -translate-y-1/2 bg-teal-600 hover:bg-teal-500 text-white text-xs rounded-lg px-3 py-1.5 disabled:opacity-40"
            :disabled="!command.trim() || loading"
            @click="submit"
          >
            {{ loading ? '...' : 'Run' }}
          </button>

          <!-- Suggestions -->
          <div
            v-if="suggestions.length && command"
            class="absolute top-full left-0 right-0 mt-1 bg-white border border-gray-200 rounded-xl overflow-hidden z-10 shadow-md"
          >
            <button
              v-for="s in suggestions"
              :key="s"
              class="w-full text-left px-4 py-2 text-xs text-gray-600 hover:bg-gray-50"
              @click="command = s"
            >
              {{ s }}
            </button>
          </div>
        </div>

        <!-- Result area -->
        <div class="flex-1 overflow-y-auto space-y-3">
          <div
            v-if="loading"
            class="text-sm text-teal-600 animate-pulse"
          >
            Thinking…
          </div>

          <div
            v-if="lastResult?.error"
            class="text-sm text-red-700 bg-red-50 border border-red-200 rounded-xl px-4 py-3"
          >
            {{ lastResult.error }}
          </div>

          <div
            v-if="lastResult?.pattern"
            class="text-sm text-emerald-700 bg-emerald-50 border border-emerald-200 rounded-xl px-4 py-2"
          >
            ✓ Applied pattern: <strong>{{ lastResult.pattern }}</strong>
          </div>

          <div
            v-if="showDiff && lastResult?.patch"
            class="space-y-2"
          >
            <p class="text-xs text-gray-500 uppercase tracking-wide">
              What changed
            </p>
            <pre class="text-xs text-green-700 bg-gray-50 border border-gray-200 rounded-xl p-3 overflow-auto max-h-32">{{ lastResult.patch }}</pre>
            <button
              class="bg-teal-600 hover:bg-teal-500 text-white text-sm rounded-xl px-4 py-2"
              @click="applyAndClose"
            >
              Apply to workflow
            </button>
          </div>

          <!-- Command history -->
          <div
            v-if="history.length && !lastResult"
            class="space-y-1"
          >
            <p class="text-xs text-gray-400 uppercase tracking-wide">
              Recent commands
            </p>
            <button
              v-for="h in history"
              :key="h"
              class="block w-full text-left text-xs text-gray-500 hover:text-gray-900 px-2 py-1 rounded hover:bg-gray-50"
              @click="command = h"
            >
              {{ h }}
            </button>
          </div>
        </div>
      </div>
    </div>
  </Transition>
</template>
