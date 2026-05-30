<script setup lang="ts">
import { ref, watch } from 'vue';
import { Star } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmModal from '@/components/common/FmModal.vue';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { connectorService, type ConnectorRatingResult } from '@/services/connector.service';

const props = defineProps<{ modelValue: boolean; workspaceId: string; connectorId: string }>();
const emit = defineEmits<{ 'update:modelValue': [value: boolean]; rated: [result: ConnectorRatingResult] }>();

const toast = useToast();
const rating = ref(0);
const review = ref('');
const submitting = ref(false);

watch(
  () => props.modelValue,
  (open) => {
    if (open) {
      rating.value = 0;
      review.value = '';
    }
  },
);

async function submit(): Promise<void> {
  if (rating.value < 1) {
    toast.error('Select a star rating from 1 to 5.');
    return;
  }
  submitting.value = true;
  try {
    const result = await connectorService.rateConnector(
      props.workspaceId,
      props.connectorId,
      rating.value,
      review.value.trim() || null,
    );
    emit('rated', result);
    emit('update:modelValue', false);
    toast.success('Thanks for your review!');
  } catch (e) {
    toast.error(toUserFacingError(e).message);
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <FmModal
    :model-value="modelValue"
    title="Rate this connector"
    size="sm"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <div class="space-y-4">
      <div class="flex items-center gap-1">
        <button
          v-for="star in 5"
          :key="star"
          type="button"
          :aria-label="`${star} star${star > 1 ? 's' : ''}`"
          @click="rating = star"
        >
          <Star
            class="h-7 w-7"
            :class="star <= rating ? 'fill-amber-400 text-amber-400' : 'text-slate-300'"
          />
        </button>
      </div>
      <div>
        <label class="mb-1 block text-sm font-medium text-slate-700">Review (optional)</label>
        <textarea
          v-model="review"
          rows="3"
          maxlength="500"
          placeholder="What did you think of this connector?"
          class="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm focus:border-primary-500 focus:outline-none"
        />
        <p class="mt-1 text-right text-xs text-slate-400">
          {{ review.length }}/500
        </p>
      </div>
    </div>
    <template #footer>
      <FmButton
        variant="secondary"
        @click="emit('update:modelValue', false)"
      >
        Cancel
      </FmButton>
      <FmButton
        :loading="submitting"
        @click="submit"
      >
        Submit review
      </FmButton>
    </template>
  </FmModal>
</template>
