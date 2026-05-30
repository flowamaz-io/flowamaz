<script setup lang="ts">
import { ref } from 'vue';
import FmButton from '@/components/common/FmButton.vue';
import FmModal from '@/components/common/FmModal.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import { workspaceService } from '@/services/workspace.service';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';

const props = defineProps<{ open: boolean; workspaceId: string; workspaceName: string }>();
const emit = defineEmits<{ close: []; archived: [] }>();

const toast = useToast();
const archiving = ref(false);

async function confirm(): Promise<void> {
  archiving.value = true;
  try {
    await workspaceService.archive(props.workspaceId);
    toast.success(`${props.workspaceName} has been archived. Restore it any time from the workspaces overview.`);
    emit('archived');
    emit('close');
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    archiving.value = false;
  }
}
</script>

<template>
  <FmModal
    :model-value="open"
    title="Archive workspace"
    size="md"
    @update:model-value="emit('close')"
  >
    <div class="space-y-4">
      <p class="text-sm text-slate-600">
        You're about to archive <span class="font-medium text-slate-900">{{ workspaceName }}</span>.
      </p>
      <FmAlert type="warning">
        Archiving pauses all running instances and disables new triggers. Existing data is preserved.
        You can restore an archived workspace.
      </FmAlert>
    </div>
    <template #footer>
      <FmButton
        variant="secondary"
        @click="emit('close')"
      >
        Cancel
      </FmButton>
      <FmButton
        variant="danger"
        :loading="archiving"
        @click="confirm"
      >
        Archive workspace
      </FmButton>
    </template>
  </FmModal>
</template>
