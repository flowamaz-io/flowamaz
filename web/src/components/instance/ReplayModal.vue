<script setup lang="ts">
import { ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import FmModal from '@/components/common/FmModal.vue';
import FmButton from '@/components/common/FmButton.vue';
import { instanceService } from '@/services/instance.service';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';

const props = defineProps<{
  modelValue: boolean;
  workspaceId: string;
  instanceId: string;
  originalPayload: string | null;
}>();

const emit = defineEmits<{ 'update:modelValue': [value: boolean] }>();

const router = useRouter();
const toast = useToast();

const payload = ref('');
const payloadError = ref<string | null>(null);
const submitting = ref(false);

function prettify(raw: string | null): string {
  if (!raw || raw.trim().length === 0) return '{}';
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}

watch(
  () => props.modelValue,
  (open) => {
    if (open) {
      payload.value = prettify(props.originalPayload);
      payloadError.value = null;
    }
  },
);

function validPayload(): boolean {
  if (payload.value.trim().length === 0) return true;
  try {
    JSON.parse(payload.value);
    return true;
  } catch {
    payloadError.value = 'The override must be valid JSON. Fix the highlighted text, or clear it to reuse the original payload.';
    return false;
  }
}

async function replay(): Promise<void> {
  payloadError.value = null;
  if (!validPayload()) return;
  submitting.value = true;
  try {
    const override = payload.value.trim().length > 0 ? payload.value : null;
    const result = await instanceService.replay(props.workspaceId, props.instanceId, override);
    toast.success(`Test replay ${result.instanceId.slice(0, 8)} started`);
    emit('update:modelValue', false);
    await router.push(`/instances/${result.instanceId}`);
  } catch (err) {
    toast.error(toUserFacingError(err).message);
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <FmModal
    :model-value="modelValue"
    title="Replay as a test run"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <div class="space-y-4">
      <div class="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
        <p class="font-medium">
          This is a test replay
        </p>
        <p class="mt-1">
          A new isolated instance runs the same workflow version with the payload below. It never
          affects production and is deleted automatically after 24 hours.
        </p>
      </div>

      <div>
        <label
          for="replay-payload"
          class="mb-1 block text-sm font-medium text-slate-700"
        >Payload (edit to override, or leave as-is)</label>
        <textarea
          id="replay-payload"
          v-model="payload"
          rows="10"
          class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 font-mono text-xs text-slate-900 focus:border-primary-500 focus:outline-none focus:ring-2 focus:ring-primary-200"
        />
        <p
          v-if="payloadError"
          class="mt-1 text-sm text-danger-600"
        >
          {{ payloadError }}
        </p>
      </div>
    </div>

    <template #footer>
      <FmButton
        variant="ghost"
        @click="emit('update:modelValue', false)"
      >
        Cancel
      </FmButton>
      <FmButton
        :loading="submitting"
        class="border border-amber-500 bg-white text-amber-700 hover:bg-amber-50"
        @click="replay"
      >
        Replay
      </FmButton>
    </template>
  </FmModal>
</template>
