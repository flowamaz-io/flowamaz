<template>
  <div class="space-y-6">
    <div>
      <h2 class="text-lg font-semibold text-gray-100 mb-1">Create from Conversation</h2>
      <p class="text-sm text-gray-400">Paste a Slack thread, email chain, or meeting notes. AI extracts the workflow.</p>
    </div>

    <!-- Source type selector -->
    <div class="flex gap-2">
      <button
        v-for="src in sources"
        :key="src.value"
        :class="[
          'px-3 py-1.5 rounded text-xs font-medium transition-colors',
          sourceType === src.value
            ? 'bg-indigo-600 text-white'
            : 'bg-gray-800 text-gray-400 hover:text-gray-200 border border-gray-700'
        ]"
        @click="sourceType = src.value"
      >
        {{ src.label }}
      </button>
    </div>

    <!-- Hint for Slack format -->
    <div v-if="sourceType === 'slack'" class="text-xs text-gray-500 p-2 bg-gray-800 rounded border border-gray-700">
      Paste Slack's exported JSON (message array) or raw thread text. Both formats work.
    </div>

    <!-- Textarea -->
    <div class="space-y-1">
      <label class="text-xs font-medium text-gray-400">Conversation text</label>
      <textarea
        v-model="text"
        rows="10"
        placeholder="Paste your Slack thread, email chain, or meeting notes here..."
        class="w-full bg-gray-800 border border-gray-600 rounded px-3 py-2 text-sm text-gray-100 outline-none focus:border-indigo-500 resize-none transition-colors font-mono placeholder-gray-600"
      />
    </div>

    <!-- Error -->
    <div v-if="error" class="p-3 bg-red-950 border border-red-700 rounded text-sm text-red-400">
      {{ error }}
    </div>

    <!-- Extracted process preview -->
    <div v-if="extractedProcess && !importing" class="p-4 bg-gray-800 border border-gray-700 rounded space-y-3">
      <div class="text-sm font-medium text-indigo-300">Extracted Process</div>
      <p class="text-sm text-gray-300">{{ extractedProcess.summary }}</p>

      <div v-if="extractedProcess.approvers?.length" class="space-y-1">
        <div class="text-xs text-gray-500 font-medium">Approvers</div>
        <div class="flex flex-wrap gap-1">
          <span v-for="a in extractedProcess.approvers" :key="a" class="text-xs px-2 py-0.5 bg-gray-700 text-gray-300 rounded">{{ a }}</span>
        </div>
      </div>

      <div v-if="extractedProcess.systems?.length" class="space-y-1">
        <div class="text-xs text-gray-500 font-medium">Systems</div>
        <div class="flex flex-wrap gap-1">
          <span v-for="s in extractedProcess.systems" :key="s" class="text-xs px-2 py-0.5 bg-indigo-950 text-indigo-300 rounded">{{ s }}</span>
        </div>
      </div>

      <div class="flex gap-3 pt-2">
        <button
          class="flex-1 py-2 px-4 rounded bg-indigo-600 hover:bg-indigo-500 text-sm font-medium text-white transition-colors"
          @click="applyToCanvas"
        >
          Apply to Canvas
        </button>
        <button
          class="py-2 px-4 rounded bg-gray-700 hover:bg-gray-600 text-sm text-gray-300 transition-colors"
          @click="reset"
        >
          Edit
        </button>
      </div>
    </div>

    <!-- Submit button -->
    <button
      v-if="!extractedProcess"
      :disabled="!text.trim() || importing"
      class="w-full py-2 px-4 rounded bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 disabled:cursor-not-allowed text-sm font-medium text-white transition-colors"
      @click="submit"
    >
      <span v-if="importing">Extracting workflow...</span>
      <span v-else>Extract Workflow</span>
    </button>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import axios from 'axios';

interface ExtractedProcess {
  summary: string;
  steps: string[];
  approvers: string[];
  systems: string[];
  generatedRequest: Record<string, string>;
}

const props = defineProps<{ workspaceId: string }>();
const emit = defineEmits<{ generated: [yaml: string]; close: [] }>();

const sources = [
  { value: 'slack', label: 'Slack' },
  { value: 'teams', label: 'Teams' },
  { value: 'email', label: 'Email' },
  { value: 'generic', label: 'General text' },
];

const sourceType = ref('generic');
const text = ref('');
const importing = ref(false);
const error = ref<string | null>(null);
const extractedProcess = ref<ExtractedProcess | null>(null);
let yamlContent = '';

async function submit() {
  if (!text.value.trim()) return;
  importing.value = true;
  error.value = null;
  extractedProcess.value = null;

  try {
    const { data } = await axios.post(`/api/v1/workspaces/${props.workspaceId}/workflows/from-conversation`, {
      text: text.value,
      source_type: sourceType.value,
    });
    yamlContent = data.yaml_content;
    extractedProcess.value = data.extracted_process;
  } catch (e) {
    const msg = (e as { response?: { data?: { error?: string } } }).response?.data?.error;
    error.value = msg ?? 'Extraction failed. Please check your input and try again.';
  } finally {
    importing.value = false;
  }
}

function applyToCanvas() {
  if (yamlContent) emit('generated', yamlContent);
}

function reset() {
  extractedProcess.value = null;
  yamlContent = '';
}
</script>
