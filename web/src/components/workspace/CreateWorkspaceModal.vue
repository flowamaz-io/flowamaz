<script setup lang="ts">
import { ref, watch } from 'vue';
import FmButton from '@/components/common/FmButton.vue';
import FmInput from '@/components/common/FmInput.vue';
import FmModal from '@/components/common/FmModal.vue';
import { useWorkspace } from '@/composables/useWorkspace';
import { toUserFacingError } from '@/utils/error.util';
import { slugify } from '@/components/workspace/createWorkspace';
import type { WorkspaceResponse } from '@/types';

const props = defineProps<{ open: boolean }>();
const emit = defineEmits<{ close: []; created: [workspace: WorkspaceResponse] }>();

const ws = useWorkspace();

const name = ref('');
const slug = ref('');
// Once the user edits the slug by hand we stop auto-syncing it from the name.
const slugTouched = ref(false);
const error = ref('');
const creating = ref(false);

watch(
  () => props.open,
  (open) => {
    if (open) {
      name.value = '';
      slug.value = '';
      slugTouched.value = false;
      error.value = '';
    }
  },
);

function onNameInput(value: string): void {
  name.value = value;
  if (!slugTouched.value) slug.value = slugify(value);
}

function onSlugInput(value: string): void {
  slugTouched.value = true;
  slug.value = slugify(value);
}

async function submit(): Promise<void> {
  if (!name.value.trim() || !slug.value) {
    error.value = 'Enter a workspace name so we can create it and generate its URL.';
    return;
  }
  creating.value = true;
  error.value = '';
  try {
    const created = await ws.createWorkspace({ name: name.value.trim(), slug: slug.value });
    emit('created', created);
    emit('close');
  } catch (e) {
    error.value = toUserFacingError(e).message;
  } finally {
    creating.value = false;
  }
}
</script>

<template>
  <FmModal
    :model-value="open"
    title="Create workspace"
    size="md"
    @update:model-value="emit('close')"
  >
    <div class="space-y-4">
      <FmInput
        :model-value="name"
        label="Workspace name"
        placeholder="e.g. Finance Ops"
        :error="error"
        @update:model-value="onNameInput"
      />
      <div>
        <FmInput
          :model-value="slug"
          label="Workspace URL"
          prefix="flowamaz.io/"
          placeholder="finance-ops"
          hint="Lowercase letters, numbers and hyphens. This becomes the workspace address."
          @update:model-value="onSlugInput"
        />
        <p class="mt-1 text-xs text-slate-500">
          flowamaz.io/{{ slug || 'your-workspace' }}
        </p>
      </div>
    </div>
    <template #footer>
      <FmButton
        variant="secondary"
        @click="emit('close')"
      >
        Cancel
      </FmButton>
      <FmButton
        :loading="creating"
        @click="submit"
      >
        Create workspace
      </FmButton>
    </template>
  </FmModal>
</template>
