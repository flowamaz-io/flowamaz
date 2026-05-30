<template>
  <Teleport to="body">
    <div
      v-if="isOpen"
      class="fixed inset-0 z-50"
    >
      <!-- Overlay -->
      <div
        class="absolute inset-0 bg-black/20 backdrop-blur-sm"
        @click="requestDiscard"
      />

      <!-- Sliding panel -->
      <Transition
        enter-active-class="transition-transform duration-300 ease-in-out"
        enter-from-class="translate-x-full"
        enter-to-class="translate-x-0"
        leave-active-class="transition-transform duration-300 ease-in-out"
        leave-from-class="translate-x-0"
        leave-to-class="translate-x-full"
      >
        <div
          v-if="isOpen"
          class="absolute right-0 top-0 h-full w-[480px] bg-white shadow-2xl flex flex-col"
        >
          <!-- Header -->
          <div class="h-14 border-b border-gray-200 flex items-center justify-between px-4 shrink-0">
            <span class="font-semibold text-gray-900">Generated Workflow</span>
            <div class="flex items-center gap-2">
              <button
                class="px-3 py-1.5 rounded bg-teal-600 hover:bg-teal-500 text-white text-sm font-medium transition-colors"
                @click="emit('openInCanvas', props.workflowId)"
              >
                Open in Canvas
              </button>
              <button
                class="p-1.5 rounded hover:bg-gray-100 text-gray-500 hover:text-gray-700 transition-colors text-lg leading-none"
                @click="requestDiscard"
              >
                ×
              </button>
            </div>
          </div>

          <!-- Discard confirmation -->
          <div
            v-if="showDiscardPrompt"
            class="mx-4 mt-4 p-3 bg-red-50 border border-red-200 rounded-lg flex items-center justify-between gap-3 shrink-0"
          >
            <span class="text-sm text-red-700">Discard this workflow? This cannot be undone.</span>
            <div class="flex gap-2 shrink-0">
              <button
                class="px-3 py-1 rounded bg-red-600 hover:bg-red-500 text-white text-xs font-medium transition-colors"
                @click="confirmDiscard"
              >
                Discard
              </button>
              <button
                class="px-3 py-1 rounded border border-gray-300 hover:bg-gray-50 text-gray-700 text-xs transition-colors"
                @click="showDiscardPrompt = false"
              >
                Keep
              </button>
            </div>
          </div>

          <!-- Body -->
          <div class="flex-1 overflow-auto p-4">
            <div class="text-xs text-gray-500 uppercase tracking-wider mb-2">
              Generated Workflow YAML
            </div>
            <div class="relative">
              <pre class="text-sm font-mono bg-gray-50 rounded-lg p-4 overflow-auto text-gray-800 border border-gray-200 whitespace-pre-wrap min-h-[200px]">{{ props.yamlContent }}</pre>
              <button
                class="absolute top-2 right-2 px-2 py-1 text-xs bg-white border border-gray-200 rounded hover:bg-gray-50 text-gray-600 transition-colors"
                @click="copyYaml"
              >
                {{ copied ? 'Copied!' : 'Copy' }}
              </button>
            </div>
          </div>

          <!-- Footer -->
          <div class="h-16 border-t border-gray-200 flex items-center gap-3 px-4 shrink-0">
            <button
              class="px-4 py-2 rounded bg-teal-600 hover:bg-teal-500 text-white text-sm font-medium transition-colors"
              @click="emit('openInCanvas', props.workflowId)"
            >
              Open in Canvas
            </button>
            <button
              class="px-3 py-2 rounded text-gray-500 hover:text-gray-700 text-sm transition-colors"
              @click="emit('regenerate', props.workflowId)"
            >
              Regenerate
            </button>
          </div>
        </div>
      </Transition>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { ref } from 'vue';

const props = defineProps<{
  yamlContent: string;
  workflowName: string;
  workflowId: string;
  isOpen: boolean;
}>();

const emit = defineEmits<{
  openInCanvas: [workflowId: string];
  regenerate: [workflowId: string];
  discard: [workflowId: string];
}>();

const copied = ref(false);
const showDiscardPrompt = ref(false);

async function copyYaml() {
  try {
    await navigator.clipboard.writeText(props.yamlContent);
    copied.value = true;
    setTimeout(() => { copied.value = false; }, 2000);
  } catch {
    // clipboard unavailable
  }
}

function requestDiscard() {
  showDiscardPrompt.value = true;
}

function confirmDiscard() {
  showDiscardPrompt.value = false;
  emit('discard', props.workflowId);
}
</script>
