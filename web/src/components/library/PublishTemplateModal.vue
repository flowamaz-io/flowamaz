<script setup lang="ts">
import { ref, watch } from 'vue';
import FmModal from '@/components/common/FmModal.vue';
import { templateService } from '@/services/template.service';

const props = defineProps<{
  modelValue: boolean;
  workspaceId: string;
  workflowId: string;
  workflowName?: string;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
  published: [];
}>();

const CATEGORIES = ['Finance', 'HR', 'IT', 'Legal', 'Operations', 'Custom'] as const;

const name = ref('');
const description = ref('');
const category = ref<(typeof CATEGORIES)[number]>('Operations');
const tagsInput = ref('');
const previewImageUrl = ref('');

const submitting = ref(false);
const error = ref<string | null>(null);

watch(
  () => props.modelValue,
  (open) => {
    if (open) {
      name.value = props.workflowName ?? '';
      description.value = '';
      category.value = 'Operations';
      tagsInput.value = '';
      previewImageUrl.value = '';
      error.value = null;
      submitting.value = false;
    }
  },
);

function close(): void {
  if (submitting.value) return;
  emit('update:modelValue', false);
}

async function onPublish(): Promise<void> {
  if (!name.value.trim()) {
    error.value = 'Give the template a name so others can find it.';
    return;
  }
  if (!description.value.trim()) {
    error.value = 'Add a short description so others know what this template does.';
    return;
  }
  submitting.value = true;
  error.value = null;
  try {
    const tags = tagsInput.value
      .split(',')
      .map((t) => t.trim())
      .filter((t) => t.length > 0);
    await templateService.publish(props.workspaceId, {
      workflowId: props.workflowId,
      name: name.value.trim(),
      description: description.value.trim(),
      category: category.value,
      tags,
      previewImageUrl: previewImageUrl.value.trim() || null,
    });
    emit('published');
    emit('update:modelValue', false);
  } catch (err: unknown) {
    error.value =
      err instanceof Error
        ? err.message
        : 'Could not publish the template. Make sure the workflow is published, then try again.';
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <FmModal
    :model-value="modelValue"
    size="md"
    title="Publish as template"
    @update:model-value="close"
  >
    <div class="space-y-4">
      <p class="text-sm text-slate-500">
        Share this workflow with the community. It will appear in the template gallery after review.
      </p>

      <div>
        <label class="mb-1 block text-sm font-medium text-slate-700">Template name</label>
        <input
          v-model="name"
          type="text"
          :disabled="submitting"
          class="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-900 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200 disabled:bg-slate-50"
        >
      </div>

      <div>
        <label class="mb-1 block text-sm font-medium text-slate-700">Description</label>
        <textarea
          v-model="description"
          rows="3"
          :disabled="submitting"
          class="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-900 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200 disabled:bg-slate-50"
        />
      </div>

      <div>
        <label class="mb-1 block text-sm font-medium text-slate-700">Category</label>
        <select
          v-model="category"
          :disabled="submitting"
          class="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200 disabled:bg-slate-50"
        >
          <option
            v-for="c in CATEGORIES"
            :key="c"
            :value="c"
          >
            {{ c }}
          </option>
        </select>
      </div>

      <div>
        <label class="mb-1 block text-sm font-medium text-slate-700">Tags</label>
        <input
          v-model="tagsInput"
          type="text"
          :disabled="submitting"
          placeholder="approval, finance, multi-step"
          class="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200 disabled:bg-slate-50"
        >
        <p class="mt-1 text-xs text-slate-400">
          Comma-separated.
        </p>
      </div>

      <div>
        <label class="mb-1 block text-sm font-medium text-slate-700">Preview image URL <span class="text-slate-400">(optional)</span></label>
        <input
          v-model="previewImageUrl"
          type="url"
          :disabled="submitting"
          placeholder="https://…"
          class="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200 disabled:bg-slate-50"
        >
      </div>

      <p
        v-if="error"
        class="text-sm text-red-600"
      >
        {{ error }}
      </p>
    </div>

    <template #footer>
      <button
        type="button"
        :disabled="submitting"
        class="rounded-lg border border-slate-200 px-4 py-2 text-sm font-medium text-slate-600 hover:bg-slate-50 disabled:opacity-50"
        @click="close"
      >
        Cancel
      </button>
      <button
        type="button"
        :disabled="submitting"
        class="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-700 disabled:opacity-50"
        @click="onPublish"
      >
        {{ submitting ? 'Publishing…' : 'Publish template' }}
      </button>
    </template>
  </FmModal>
</template>
