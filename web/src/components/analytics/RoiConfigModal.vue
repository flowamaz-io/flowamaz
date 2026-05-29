<script setup lang="ts">
import { ref, watch } from 'vue';
import FmModal from '@/components/common/FmModal.vue';
import FmButton from '@/components/common/FmButton.vue';
import { analyticsService } from '@/services/analytics.service';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';

const props = defineProps<{
  modelValue: boolean;
  workspaceId: string;
  workflowId: string;
  workflowName: string;
}>();

const emit = defineEmits<{ 'update:modelValue': [value: boolean]; saved: [] }>();

const toast = useToast();
const currencies = ['MYR', 'USD', 'SGD', 'AUD', 'EUR', 'GBP'];

const manualMinutes = ref(0);
const manualCost = ref(0);
const automationCost = ref(0);
const currency = ref('MYR');
const loading = ref(false);
const saving = ref(false);

watch(
  () => props.modelValue,
  async (open) => {
    if (!open) return;
    loading.value = true;
    try {
      const existing = await analyticsService.getRoiConfig(props.workspaceId, props.workflowId);
      manualMinutes.value = existing?.manualProcessTimeMinutes ?? 0;
      manualCost.value = existing?.manualProcessCostPerRunUsd ?? 0;
      automationCost.value = existing?.automationCostPerRunUsd ?? 0;
      currency.value = existing?.monthlyCurrency ?? 'MYR';
    } finally {
      loading.value = false;
    }
  },
);

async function save(): Promise<void> {
  saving.value = true;
  try {
    await analyticsService.saveRoiConfig(props.workspaceId, props.workflowId, {
      manualProcessTimeMinutes: manualMinutes.value,
      manualProcessCostPerRunUsd: manualCost.value,
      automationCostPerRunUsd: automationCost.value,
      monthlyCurrency: currency.value,
    });
    toast.success('ROI baseline saved.');
    emit('saved');
    emit('update:modelValue', false);
  } catch (err) {
    toast.error(toUserFacingError(err).message);
  } finally {
    saving.value = false;
  }
}
</script>

<template>
  <FmModal
    :model-value="modelValue"
    :title="`Configure ROI — ${workflowName}`"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <div
      v-if="loading"
      class="py-6 text-center text-sm text-slate-400"
    >
      Loading current baseline…
    </div>
    <div
      v-else
      class="space-y-4"
    >
      <div>
        <label class="mb-1 block text-sm font-medium text-slate-700">
          How long did this process take manually?
        </label>
        <div class="flex items-center gap-2">
          <input
            v-model.number="manualMinutes"
            type="number"
            min="0"
            class="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm focus:border-primary-500 focus:outline-none"
          >
          <span class="text-sm text-slate-500">minutes / run</span>
        </div>
      </div>

      <div>
        <label class="mb-1 block text-sm font-medium text-slate-700">
          Fully-loaded cost per manual run
        </label>
        <input
          v-model.number="manualCost"
          type="number"
          min="0"
          step="0.01"
          class="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm focus:border-primary-500 focus:outline-none"
        >
      </div>

      <div>
        <label class="mb-1 block text-sm font-medium text-slate-700">
          Automated cost per run (connectors + AI + platform)
        </label>
        <input
          v-model.number="automationCost"
          type="number"
          min="0"
          step="0.01"
          class="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm focus:border-primary-500 focus:outline-none"
        >
      </div>

      <div>
        <label class="mb-1 block text-sm font-medium text-slate-700">
          Currency
        </label>
        <select
          v-model="currency"
          class="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm focus:border-primary-500 focus:outline-none"
        >
          <option
            v-for="c in currencies"
            :key="c"
            :value="c"
          >
            {{ c }}
          </option>
        </select>
      </div>

      <p class="text-xs text-slate-400">
        ROI is calculated deterministically: time saved and cost avoided come from these figures
        multiplied by your actual successful run counts.
      </p>
    </div>

    <template #footer>
      <FmButton
        variant="secondary"
        @click="emit('update:modelValue', false)"
      >
        Cancel
      </FmButton>
      <FmButton
        :loading="saving"
        @click="save"
      >
        Save baseline
      </FmButton>
    </template>
  </FmModal>
</template>
