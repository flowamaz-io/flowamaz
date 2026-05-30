<template>
  <div class="flex flex-col gap-6">
    <div>
      <h2 class="text-lg font-semibold text-slate-900 mb-1">
        Create Workflow from Description
      </h2>
      <p class="text-sm text-slate-500">
        Fill in each section. Use the microphone to speak instead of type.
      </p>
    </div>

    <div class="space-y-4">
      <!-- Workflow Name -->
      <div class="space-y-1">
        <label class="text-xs font-medium text-gray-700">Workflow Name</label>
        <input
          v-model="form.workflowName"
          placeholder="e.g. Purchase Approval"
          class="w-full bg-white border border-gray-300 rounded-lg px-3 py-2 text-sm text-gray-900 outline-none focus:ring-2 focus:ring-teal-500 focus:border-teal-500 transition-colors placeholder-gray-400"
        >
      </div>

      <!-- Purpose -->
      <FormField
        v-model="form.purpose"
        label="Purpose"
        hint="What does this workflow do and who uses it? e.g. 'Routes purchase requests from employees to their manager for approval before submitting to finance.'"
        placeholder="What does this workflow do? Who uses it?"
        :rows="2"
        @voice-transcript="(t) => (form.purpose += t)"
      />

      <!-- Trigger -->
      <FormField
        v-model="form.triggerDescription"
        label="Trigger"
        hint="What starts this workflow? e.g. 'A form submission with fields: requester name, amount, description, and cost centre.'"
        placeholder="What starts this workflow? Manual, webhook, schedule, event? What inputs are needed?"
        :rows="2"
        @voice-transcript="(t) => (form.triggerDescription += t)"
      />

      <!-- Steps -->
      <FormField
        v-model="form.stepsDescription"
        label="Steps"
        hint="Describe each step in order, including decisions and who does what. e.g. '1. Validate the requester is an active employee. 2. If amount > RM5000, route to manager approval. 3. Manager approves or rejects. 4. If approved, create PO in system.'"
        placeholder="Describe each step in order. Include decisions, branches, and who does what."
        :rows="4"
        @voice-transcript="(t) => (form.stepsDescription += t)"
      />

      <!-- Rules -->
      <FormField
        v-model="form.rulesAndConstraints"
        label="Rules & Constraints"
        hint="SLA deadlines, approval thresholds, retry rules. e.g. 'Manager must respond within 48 hours. If no response, escalate to department head. Retry failed steps 3 times.'"
        placeholder="SLA thresholds, approval limits, failure handling, retries, timeouts..."
        :rows="2"
        @voice-transcript="(t) => (form.rulesAndConstraints += t)"
      />

      <!-- Systems & AI -->
      <FormField
        v-model="form.systemsAndAi"
        label="Systems & AI"
        hint="Which external systems are involved? Any AI processing needed? e.g. 'SAP for PO creation, Slack for notifications, no AI needed.'"
        placeholder="Which systems are involved? Slack, SAP, Salesforce? Any AI steps needed?"
        :rows="2"
        @voice-transcript="(t) => (form.systemsAndAi += t)"
      />

      <!-- Existing Context (optional) -->
      <div class="space-y-1">
        <label class="text-xs font-medium text-gray-700">Existing Context <span class="text-gray-400">(optional)</span></label>
        <p class="text-sm text-gray-500 mb-2">
          Optional. Paste a related email, ticket, or document excerpt to give the AI more context.
        </p>
        <textarea
          v-model="form.existingContext"
          placeholder="Paste any existing conversation, ticket, or document extract..."
          rows="2"
          class="bg-white text-gray-900 border border-gray-300 placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-teal-500 focus:border-teal-500 rounded-lg p-3 w-full resize-none"
        />
      </div>
    </div>

    <!-- Error -->
    <div
      v-if="error"
      class="p-3 bg-red-50 border border-red-200 rounded text-sm text-red-700"
    >
      {{ error }}
    </div>

    <!-- Progress while generating -->
    <div
      v-if="generating"
      class="p-3 bg-gray-50 border border-gray-200 rounded text-sm text-teal-600"
    >
      Generating your workflow with AI... (10–15 seconds)
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
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';
import { useRouter } from 'vue-router';
import { createWorkflow, type NlWorkflowRequest } from '@/services/creation.service';

const props = defineProps<{ workspaceId: string }>();

const router = useRouter();

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

const canSubmit = computed(() =>
  form.value.workflowName.trim() &&
  form.value.purpose.trim() &&
  form.value.stepsDescription.trim()
);

async function submit() {
  if (!canSubmit.value) return;
  generating.value = true;
  error.value = null;

  try {
    const result = await createWorkflow(props.workspaceId, {
      name: form.value.workflowName,
      method: 'nl',
      nlRequest: form.value,
    });
    await router.push({ name: 'workflow-editor', params: { id: result.workflowId }, query: { workspaceId: props.workspaceId } });
  } catch (e) {
    error.value = (e as Error).message ?? 'Generation failed. Please try again.';
    generating.value = false;
  }
}
</script>

<!-- FormField is a local sub-component rendered via dynamic component -->
<script lang="ts">
import { defineComponent, h } from 'vue';
import VoiceInputButtonComp from './VoiceInputButton.vue';

export const FormField = defineComponent({
  name: 'FormField',
  props: {
    label: { type: String, required: true },
    hint: { type: String, default: '' },
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
        ...(props.hint ? [h('p', { class: 'text-sm text-gray-500 mb-2' }, props.hint)] : []),
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
