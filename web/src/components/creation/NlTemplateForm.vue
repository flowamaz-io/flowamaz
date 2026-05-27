<template>
  <div class="flex flex-col gap-6">
    <div>
      <h2 class="text-lg font-semibold text-slate-900 mb-1">Create Workflow from Description</h2>
      <p class="text-sm text-slate-500">Fill in each section. Use the microphone to speak instead of type.</p>
    </div>

    <div class="space-y-4">
      <!-- Workflow Name -->
      <div class="space-y-1">
        <label class="text-xs font-medium text-gray-700">Workflow Name</label>
        <input
          v-model="form.workflowName"
          placeholder="e.g. Purchase Approval"
          class="w-full bg-white border border-gray-300 rounded-lg px-3 py-2 text-sm text-gray-900 outline-none focus:ring-2 focus:ring-teal-500 focus:border-teal-500 transition-colors placeholder-gray-400"
        />
      </div>

      <!-- Purpose -->
      <FormField
        label="Purpose"
        placeholder="What does this workflow do? Who uses it?"
        v-model="form.purpose"
        :rows="2"
        @voice-transcript="(t) => (form.purpose += t)"
      />

      <!-- Trigger -->
      <FormField
        label="Trigger"
        placeholder="What starts this workflow? Manual, webhook, schedule, event? What inputs are needed?"
        v-model="form.triggerDescription"
        :rows="2"
        @voice-transcript="(t) => (form.triggerDescription += t)"
      />

      <!-- Steps -->
      <FormField
        label="Steps"
        placeholder="Describe each step in order. Include decisions, branches, and who does what."
        v-model="form.stepsDescription"
        :rows="4"
        @voice-transcript="(t) => (form.stepsDescription += t)"
      />

      <!-- Rules -->
      <FormField
        label="Rules & Constraints"
        placeholder="SLA thresholds, approval limits, failure handling, retries, timeouts..."
        v-model="form.rulesAndConstraints"
        :rows="2"
        @voice-transcript="(t) => (form.rulesAndConstraints += t)"
      />

      <!-- Systems & AI -->
      <FormField
        label="Systems & AI"
        placeholder="Which systems are involved? Slack, SAP, Salesforce? Any AI steps needed?"
        v-model="form.systemsAndAi"
        :rows="2"
        @voice-transcript="(t) => (form.systemsAndAi += t)"
      />

      <!-- Existing Context (optional) -->
      <div class="space-y-1">
        <label class="text-xs font-medium text-gray-700">Existing Context <span class="text-gray-400">(optional)</span></label>
        <textarea
          v-model="form.existingContext"
          placeholder="Paste any existing conversation, ticket, or document extract..."
          rows="2"
          class="bg-white text-gray-900 border border-gray-300 placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-teal-500 focus:border-teal-500 rounded-lg p-3 w-full resize-none"
        />
      </div>
    </div>

    <!-- Error -->
    <div v-if="error" class="p-3 bg-red-950 border border-red-700 rounded text-sm text-red-400">
      {{ error }}
    </div>

    <!-- Progress while generating -->
    <div v-if="generating" class="p-3 bg-gray-50 border border-gray-200 rounded text-sm font-mono text-gray-700 max-h-48 overflow-y-auto">
      <div class="text-xs text-teal-600 mb-1">Generating workflow...</div>
      <pre class="text-xs text-gray-400 whitespace-pre-wrap">{{ yamlPreview }}</pre>
    </div>

    <!-- Actions -->
    <div class="flex gap-3">
      <button
        :disabled="!canSubmit || generating"
        class="flex-1 py-2 px-4 rounded bg-teal-600 hover:bg-teal-500 disabled:opacity-40 disabled:cursor-not-allowed text-sm font-medium text-white transition-colors"
        @click="submit"
      >
        <span v-if="generating">Generating...</span>
        <span v-else>Generate Workflow</span>
      </button>
      <button
        v-if="generating"
        class="py-2 px-4 rounded bg-gray-200 hover:bg-gray-300 text-sm text-gray-700 transition-colors"
        @click="abort"
      >
        Cancel
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';
import { creationService, type NlWorkflowRequest } from '@/services/creation.service';

const props = defineProps<{ workspaceId: string }>();
const emit = defineEmits<{ generated: [yaml: string]; close: [] }>();

const form = ref<NlWorkflowRequest>({
  workflowName: '',
  purpose: '',
  triggerDescription: '',
  stepsDescription: '',
  rulesAndConstraints: '',
  systemsAndAi: '',
  existingContext: '',
});

const generating = ref(false);
const error = ref<string | null>(null);
const yamlPreview = ref('');
let abortController: AbortController | null = null;

const canSubmit = computed(() =>
  form.value.workflowName.trim() &&
  form.value.purpose.trim() &&
  form.value.stepsDescription.trim()
);

async function submit() {
  if (!canSubmit.value) return;
  generating.value = true;
  error.value = null;
  yamlPreview.value = '';
  abortController = new AbortController();

  try {
    const result = await creationService.generateWorkflow(
      props.workspaceId,
      form.value,
      (line) => { yamlPreview.value += line + '\n'; },
      abortController.signal,
    );
    emit('generated', result.yaml_content);
  } catch (e) {
    if ((e as Error).name !== 'AbortError') {
      error.value = (e as Error).message;
    }
  } finally {
    generating.value = false;
    abortController = null;
  }
}

function abort() {
  abortController?.abort();
}

// Inline FormField component declared locally to keep file self-contained
</script>

<!-- FormField is a local sub-component rendered via dynamic component -->
<script lang="ts">
import { defineComponent, h } from 'vue';
import VoiceInputButtonComp from './VoiceInputButton.vue';

export const FormField = defineComponent({
  name: 'FormField',
  props: {
    label: { type: String, required: true },
    placeholder: { type: String, default: '' },
    modelValue: { type: String, default: '' },
    rows: { type: Number, default: 2 },
  },
  emits: ['update:modelValue', 'voiceTranscript'],
  setup(props, { emit }) {
    return () =>
      h('div', { class: 'space-y-1' }, [
        h('div', { class: 'flex items-center gap-1' }, [
          h('label', { class: 'text-xs font-medium text-gray-700 flex-1' }, props.label),
          h(VoiceInputButtonComp, {
            onTranscript: (t: string) => emit('voiceTranscript', t),
          }),
        ]),
        h('textarea', {
          value: props.modelValue,
          placeholder: props.placeholder,
          rows: props.rows,
          class: 'bg-white text-gray-900 border border-gray-300 placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-teal-500 focus:border-teal-500 rounded-lg p-3 w-full resize-none',
          onInput: (e: Event) => emit('update:modelValue', (e.target as HTMLTextAreaElement).value),
        }),
      ]);
  },
});
</script>
