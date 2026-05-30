<script setup lang="ts">
import { ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { AlertTriangle } from 'lucide-vue-next';
import FmModal from '@/components/common/FmModal.vue';
import { workflowService } from '@/services/workflow.service';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';

const props = defineProps<{
  modelValue: boolean;
  workspaceId: string;
  workflowId: string;
  workflowName: string;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
  deleted: [];
}>();

const router = useRouter();
const toast = useToast();

const deleting = ref(false);
const errorMessage = ref<string | null>(null);

// Reset the inline error whenever the modal is reopened.
watch(
  () => props.modelValue,
  (open) => {
    if (open) errorMessage.value = null;
  },
);

function close(): void {
  emit('update:modelValue', false);
}

async function confirmDelete(): Promise<void> {
  deleting.value = true;
  errorMessage.value = null;
  try {
    await workflowService.remove(props.workspaceId, props.workflowId);
    emit('deleted');
    close();
    toast.success(`'${props.workflowName}' was deleted.`);
    await router.push('/workflows');
  } catch (err) {
    errorMessage.value = toUserFacingError(err).message;
  } finally {
    deleting.value = false;
  }
}
</script>

<template>
  <FmModal
    :model-value="modelValue"
    size="sm"
    title="Delete workflow"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <div class="flex gap-3">
      <span class="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-red-100 text-red-600">
        <AlertTriangle class="h-5 w-5" />
      </span>
      <div class="space-y-3">
        <p class="text-sm text-slate-700">
          Are you sure you want to delete '<span class="font-semibold text-slate-900">{{ workflowName }}</span>'?
          This action cannot be undone.
        </p>
        <p
          v-if="errorMessage"
          class="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700"
        >
          {{ errorMessage }}
        </p>
      </div>
    </div>

    <template #footer>
      <button
        type="button"
        :disabled="deleting"
        class="rounded-lg border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-50"
        @click="close"
      >
        Cancel
      </button>
      <button
        type="button"
        :disabled="deleting"
        class="rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white hover:bg-red-700 disabled:opacity-50"
        @click="confirmDelete"
      >
        {{ deleting ? 'Deleting…' : 'Delete workflow' }}
      </button>
    </template>
  </FmModal>
</template>
