<template>
  <div class="space-y-6">
    <div>
      <h2 class="text-lg font-semibold text-gray-900 mb-1">
        Create from Conversation
      </h2>
      <p class="text-sm text-gray-600">
        Paste a Slack thread, Teams transcript, email chain, or meeting notes. You'll check what was read out of it before the workflow is generated.
      </p>
    </div>

    <template v-if="stage === 'input' || stage === 'extracting'">
      <!-- Source type selector -->
      <div
        class="flex gap-2"
        role="radiogroup"
        aria-label="Where the conversation is from"
      >
        <button
          v-for="src in sources"
          :key="src.value"
          type="button"
          role="radio"
          :aria-checked="sourceType === src.value"
          :class="[
            'px-3 py-1.5 rounded text-xs font-medium transition-colors',
            sourceType === src.value
              ? 'bg-teal-600 text-white'
              : 'bg-white text-gray-600 hover:text-gray-800 border border-gray-200'
          ]"
          @click="sourceType = src.value"
        >
          {{ src.label }}
        </button>
      </div>

      <div class="text-xs text-gray-500 p-2 bg-white rounded border border-gray-200">
        {{ hint }}
      </div>

      <!-- Textarea -->
      <div class="space-y-1">
        <label
          for="conversation-text"
          class="text-xs font-medium text-gray-600"
        >Conversation text</label>
        <textarea
          id="conversation-text"
          v-model="text"
          rows="10"
          :disabled="stage === 'extracting'"
          placeholder="Paste your Slack thread, email chain, or meeting notes here..."
          class="w-full bg-white border border-gray-300 rounded px-3 py-2 text-sm text-gray-900 outline-none focus:border-teal-500 resize-none transition-colors font-mono placeholder-gray-400"
        />
      </div>
    </template>

    <!-- Error -->
    <div
      v-if="error"
      role="alert"
      class="p-3 bg-red-50 border border-red-200 rounded text-sm text-red-700"
    >
      {{ error }}
    </div>

    <!-- Working -->
    <div
      v-if="stage === 'extracting' || stage === 'creating'"
      class="flex items-center gap-3 p-3 bg-white border border-gray-200 rounded text-sm text-teal-700"
      role="status"
    >
      <div class="h-4 w-4 shrink-0 animate-spin rounded-full border-2 border-teal-500 border-t-transparent" />
      {{ stage === 'extracting' ? 'Reading the process out of the conversation… (10–15 seconds)' : 'Generating your workflow… (10–20 seconds)' }}
    </div>

    <button
      v-if="stage === 'input'"
      type="button"
      :disabled="!text.trim()"
      class="w-full py-2 px-4 rounded bg-teal-600 hover:bg-teal-500 disabled:opacity-40 disabled:cursor-not-allowed text-sm font-medium text-white transition-colors"
      @click="extract"
    >
      Extract process
    </button>

    <!-- Review -->
    <template v-if="(stage === 'review' || stage === 'creating') && extraction">
      <ConversationReview
        v-model="extraction"
        :warnings="warnings"
      />

      <ul
        v-if="problems.length > 0"
        class="space-y-1 text-sm text-amber-700"
      >
        <li
          v-for="p in problems"
          :key="p"
        >
          {{ p }}
        </li>
      </ul>

      <div class="flex gap-2">
        <button
          type="button"
          :disabled="stage === 'creating'"
          class="py-2 px-4 rounded border border-gray-300 text-sm font-medium text-gray-700 hover:bg-gray-100 disabled:opacity-40"
          @click="backToText"
        >
          Back to the conversation
        </button>
        <button
          type="button"
          :disabled="problems.length > 0 || stage === 'creating'"
          class="flex-1 py-2 px-4 rounded bg-teal-600 hover:bg-teal-500 disabled:opacity-40 disabled:cursor-not-allowed text-sm font-medium text-white transition-colors"
          data-test="create-from-conversation"
          @click="create"
        >
          Generate workflow
        </button>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import ConversationReview from './ConversationReview.vue';
import {
  createWorkflow,
  previewConversation,
  type ConversationExtraction,
  type ConversationSource,
} from '@/services/creation.service';
import { cloneExtraction, conversationProblems, toSubmittable } from '@/utils/conversationReview.util';
import { toUserFacingError } from '@/utils/error.util';

const props = defineProps<{ workspaceId: string; workflowName: string }>();

const router = useRouter();

const sources: { value: ConversationSource; label: string }[] = [
  { value: 'slack', label: 'Slack' },
  { value: 'teams', label: 'Teams' },
  { value: 'email', label: 'Email' },
  { value: 'generic', label: 'General text' },
];

const HINTS: Record<ConversationSource, string> = {
  slack: "Paste Slack's exported JSON (a message array, or an API response with \"messages\") or the copied thread. Both work.",
  teams: 'Paste a meeting transcript (.vtt, with speaker names) or copied chat messages.',
  email: 'Paste the whole chain, replies included. Quoted text and signatures are ignored; messages are put in date order.',
  generic: 'Paste meeting notes or any text. Lines like "Anita: …" are read as who said what.',
};

type Stage = 'input' | 'extracting' | 'review' | 'creating';
const stage = ref<Stage>('input');
const sourceType = ref<ConversationSource>('generic');
const text = ref('');
const error = ref<string | null>(null);
const extraction = ref<ConversationExtraction | null>(null);
const warnings = ref<string[]>([]);

const hint = computed(() => HINTS[sourceType.value]);
// An error is about the text that was sent; once it changes, the message no longer applies.
watch([text, sourceType], () => {
  if (stage.value === 'input') error.value = null;
});
const problems = computed(() => (extraction.value ? conversationProblems(extraction.value) : []));

async function extract(): Promise<void> {
  if (!text.value.trim()) return;
  stage.value = 'extracting';
  error.value = null;
  try {
    const preview = await previewConversation(props.workspaceId, text.value, sourceType.value);
    extraction.value = cloneExtraction(preview.extraction);
    warnings.value = preview.warnings;
    stage.value = 'review';
  } catch (e) {
    error.value = toUserFacingError(e).message;
    stage.value = 'input';
  }
}

function backToText(): void {
  stage.value = 'input';
  error.value = null;
}

async function create(): Promise<void> {
  if (!extraction.value || problems.value.length > 0) return;
  stage.value = 'creating';
  error.value = null;
  const confirmed = toSubmittable(extraction.value);
  try {
    const result = await createWorkflow(props.workspaceId, {
      // A name typed above wins; otherwise the (reviewed) name from the conversation.
      name: props.workflowName.trim() || confirmed.workflowName,
      method: 'conversation',
      conversationExtraction: confirmed,
    });
    await router.push({ name: 'workflow-editor', params: { id: result.workflowId }, query: { workspaceId: props.workspaceId } });
  } catch (e) {
    error.value = toUserFacingError(e).message;
    stage.value = 'review';
  }
}

defineExpose({ retrigger: extract });
</script>
