<script setup lang="ts">
import { ref, watch } from 'vue';
import FmModal from '@/components/common/FmModal.vue';
import type { TemplateListItem } from '@/services/template.service';

const props = defineProps<{
  modelValue: boolean;
  template: TemplateListItem | null;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
  confirm: [name: string];
}>();

const name = ref('');
const installing = ref(false);
const error = ref<string | null>(null);

// Pre-fill the name from the template whenever the modal opens for a new template.
watch(
  () => props.template,
  (t) => {
    if (t) name.value = t.name;
  },
  { immediate: true },
);

watch(
  () => props.modelValue,
  (open) => {
    if (open && props.template) {
      name.value = props.template.name;
      error.value = null;
      installing.value = false;
    }
  },
);

async function onInstall(): Promise<void> {
  if (!name.value.trim()) {
    error.value = 'Give your workflow a name so you can find it later.';
    return;
  }
  installing.value = true;
  error.value = null;
  emit('confirm', name.value.trim());
}

function close(): void {
  if (installing.value) return;
  emit('update:modelValue', false);
}

// Allow the parent to surface an install failure and re-enable the form.
function fail(message: string): void {
  installing.value = false;
  error.value = message;
}

defineExpose({ fail });
</script>

<template>
  <FmModal
    :model-value="modelValue"
    size="sm"
    title="Install template"
    @update:model-value="close"
  >
    <div class="space-y-4">
      <p class="text-sm text-slate-500">
        What would you like to name this workflow?
      </p>
      <input
        v-model="name"
        type="text"
        :disabled="installing"
        placeholder="e.g. Q3 Purchase Approval"
        class="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200 disabled:bg-slate-50"
        @keyup.enter="onInstall"
      >
      <p
        v-if="error"
        class="text-sm text-red-600"
      >
        {{ error }}
      </p>
      <p
        v-if="installing"
        class="text-sm text-slate-500"
      >
        Installing template…
      </p>
    </div>

    <template #footer>
      <button
        type="button"
        :disabled="installing"
        class="rounded-lg border border-slate-200 px-4 py-2 text-sm font-medium text-slate-600 hover:bg-slate-50 disabled:opacity-50"
        @click="close"
      >
        Cancel
      </button>
      <button
        type="button"
        :disabled="installing"
        class="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-700 disabled:opacity-50"
        @click="onInstall"
      >
        {{ installing ? 'Installing…' : 'Install' }}
      </button>
    </template>
  </FmModal>
</template>
