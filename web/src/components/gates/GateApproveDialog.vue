<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import FmModal from '@/components/common/FmModal.vue';
import FmButton from '@/components/common/FmButton.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import GateFormFields from './GateFormFields.vue';
import { answerProblems, answersToSend, initialValues } from '@/utils/gateForm.util';
import type { GateFormField, GateFormValue } from '@/types';

// Approving a gate that has a form (FUNCTIONAL.md §23.2): the approver fills in the fields, then approves.
const props = defineProps<{
  modelValue: boolean;
  gateName: string;
  fields: GateFormField[];
  submitting: boolean;
  /** The server's message when it refused the answers. */
  serverError: string | null;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
  approve: [values: Record<string, GateFormValue>, note: string | undefined];
}>();

const values = ref<Record<string, GateFormValue>>({});
const note = ref('');
const tried = ref(false);

watch(() => props.modelValue, (open) => {
  if (!open) return;
  values.value = initialValues(props.fields);
  note.value = '';
  tried.value = false;
}, { immediate: true });

const problems = computed(() => (tried.value ? answerProblems(props.fields, values.value) : {}));

function submit(): void {
  tried.value = true;
  if (Object.keys(answerProblems(props.fields, values.value)).length > 0) return;
  emit('approve', answersToSend(props.fields, values.value), note.value.trim() || undefined);
}
</script>

<template>
  <FmModal
    :model-value="modelValue"
    size="md"
    :title="`Approve: ${gateName}`"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <form
      class="space-y-4"
      data-testid="gate-approve-form"
      @submit.prevent="submit"
    >
      <p class="text-sm text-slate-600">
        This approval asks for a few details. They go on to the next steps of the workflow.
      </p>
      <FmAlert
        v-if="serverError"
        type="error"
      >
        {{ serverError }}
      </FmAlert>
      <GateFormFields
        v-model="values"
        :fields="fields"
        :problems="problems"
        :disabled="submitting"
      />
      <div>
        <label
          for="gate-approve-note"
          class="mb-1 block text-sm font-medium text-slate-700"
        >Note (optional)</label>
        <input
          id="gate-approve-note"
          v-model="note"
          type="text"
          class="w-full rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-800 focus:border-primary-400 focus:outline-none focus:ring-2 focus:ring-primary-100"
        >
      </div>
    </form>
    <template #footer>
      <div class="flex justify-end gap-2">
        <FmButton
          variant="secondary"
          :disabled="submitting"
          @click="emit('update:modelValue', false)"
        >
          Cancel
        </FmButton>
        <FmButton
          :loading="submitting"
          data-testid="gate-approve-submit"
          @click="submit"
        >
          Approve
        </FmButton>
      </div>
    </template>
  </FmModal>
</template>
