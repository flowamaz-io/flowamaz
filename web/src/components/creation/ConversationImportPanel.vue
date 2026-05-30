<template>
  <div class="space-y-6">
    <div>
      <h2 class="text-lg font-semibold text-gray-100 mb-1">
        Create from Conversation
      </h2>
      <p class="text-sm text-gray-400">
        Paste a Slack thread, email chain, or meeting notes. AI extracts the workflow.
      </p>
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
    <div
      v-if="sourceType === 'slack'"
      class="text-xs text-gray-500 p-2 bg-gray-800 rounded border border-gray-700"
    >
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
    <div
      v-if="error"
      class="p-3 bg-red-950 border border-red-700 rounded text-sm text-red-400"
    >
      {{ error }}
    </div>

    <!-- Loading -->
    <div
      v-if="importing"
      class="p-3 bg-gray-800 border border-gray-700 rounded text-sm text-indigo-400"
    >
      Extracting process from conversation... (10–15 seconds)
    </div>

    <!-- Submit button -->
    <button
      v-if="!importing"
      :disabled="!text.trim()"
      class="w-full py-2 px-4 rounded bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 disabled:cursor-not-allowed text-sm font-medium text-white transition-colors"
      @click="submit"
    >
      Extract Workflow
    </button>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import { createWorkflow } from '@/services/creation.service';

const props = defineProps<{ workspaceId: string; workflowName: string }>();

const router = useRouter();

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

async function submit() {
  if (!text.value.trim()) return;
  importing.value = true;
  error.value = null;

  try {
    const result = await createWorkflow(props.workspaceId, {
      name: props.workflowName || 'Untitled Workflow',
      method: 'conversation',
      conversationText: text.value,
      sourceType: sourceType.value,
    });
    await router.push({ name: 'workflow-editor', params: { id: result.workflowId }, query: { workspaceId: props.workspaceId } });
  } catch (e) {
    const msg = (e as { response?: { data?: { message?: string } } }).response?.data?.message;
    error.value = msg ?? 'Extraction failed. Please check your input and try again.';
    importing.value = false;
  }
}

defineExpose({ retrigger: submit });
</script>
