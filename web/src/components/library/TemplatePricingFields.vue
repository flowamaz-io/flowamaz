<script setup lang="ts">
import { computed } from 'vue';
import { SUPPORTED_CURRENCIES, type PricingDraft, type PricingModel } from '@/types/marketplace';
import { PRICING_MODEL_LABELS, formatMoney, majorToMinor, platformFee, publisherNet } from '@/utils/money.util';

const props = withDefaults(
  defineProps<{
    modelValue: PricingDraft;
    feePercent?: number;
    disabled?: boolean;
    /** Compact layout for inline table editing. */
    compact?: boolean;
    error?: string | null;
  }>(),
  { feePercent: 20, disabled: false, compact: false, error: null },
);

const emit = defineEmits<{ 'update:modelValue': [value: PricingDraft] }>();

const MODELS: PricingModel[] = ['free', 'one_time', 'monthly'];

function update(patch: Partial<PricingDraft>): void {
  emit('update:modelValue', { ...props.modelValue, ...patch });
}

const priceCents = computed<number | null>(() =>
  props.modelValue.pricingModel === 'free' ? null : majorToMinor(props.modelValue.amount, props.modelValue.currency),
);

const earningsLine = computed<string | null>(() => {
  const cents = priceCents.value;
  if (cents === null || cents <= 0) return null;
  const net = formatMoney(publisherNet(cents, props.feePercent), props.modelValue.currency);
  const fee = formatMoney(platformFee(cents, props.feePercent), props.modelValue.currency);
  const suffix = props.modelValue.pricingModel === 'monthly' ? ' per month' : ' per sale';
  return `You receive ${net}${suffix} after the ${props.feePercent}% Flowamaz fee (${fee}).`;
});
</script>

<template>
  <div :class="compact ? 'space-y-2' : 'space-y-3'">
    <div
      role="radiogroup"
      aria-label="Pricing model"
      class="inline-flex rounded-lg border border-slate-200 bg-slate-50 p-0.5"
    >
      <button
        v-for="m in MODELS"
        :key="m"
        type="button"
        role="radio"
        :aria-checked="modelValue.pricingModel === m"
        :disabled="disabled"
        :class="[
          'rounded-md px-3 py-1.5 text-xs font-medium transition-colors disabled:opacity-50',
          modelValue.pricingModel === m ? 'bg-white text-slate-900 shadow-sm' : 'text-slate-500 hover:text-slate-700',
        ]"
        @click="update({ pricingModel: m })"
      >
        {{ PRICING_MODEL_LABELS[m] }}
      </button>
    </div>

    <div
      v-if="modelValue.pricingModel !== 'free'"
      class="flex flex-wrap items-end gap-2"
    >
      <div class="min-w-0 flex-1">
        <label
          v-if="!compact"
          class="mb-1 block text-sm font-medium text-slate-700"
        >
          Price {{ modelValue.pricingModel === 'monthly' ? 'per month' : '' }}
        </label>
        <input
          :value="modelValue.amount"
          type="text"
          inputmode="decimal"
          :disabled="disabled"
          placeholder="e.g. 49 or 19.99"
          aria-label="Price"
          :class="[
            'w-full rounded-lg border px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:outline-none focus:ring-1 disabled:bg-slate-50',
            error ? 'border-danger-400 focus:ring-danger-200' : 'border-slate-200 focus:border-indigo-400 focus:ring-indigo-200',
          ]"
          @input="update({ amount: ($event.target as HTMLInputElement).value })"
        >
      </div>
      <div>
        <label
          v-if="!compact"
          class="mb-1 block text-sm font-medium text-slate-700"
        >Currency</label>
        <select
          :value="modelValue.currency"
          :disabled="disabled"
          aria-label="Currency"
          class="rounded-lg border border-slate-200 px-3 py-2 text-sm uppercase text-slate-700 focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200 disabled:bg-slate-50"
          @change="update({ currency: ($event.target as HTMLSelectElement).value as PricingDraft['currency'] })"
        >
          <option
            v-for="c in SUPPORTED_CURRENCIES"
            :key="c"
            :value="c"
          >
            {{ c.toUpperCase() }}
          </option>
        </select>
      </div>
    </div>

    <p
      v-if="error"
      class="text-xs text-danger-600"
    >
      {{ error }}
    </p>
    <p
      v-else-if="earningsLine"
      class="text-xs text-slate-500"
      data-test="earnings-line"
    >
      {{ earningsLine }}
    </p>
    <p
      v-else-if="modelValue.pricingModel !== 'free' && !compact"
      class="text-xs text-slate-400"
    >
      Prices range from 1.00 to 10,000.00 in the selected currency. Flowamaz keeps {{ feePercent }}%; you keep the rest.
    </p>
  </div>
</template>
