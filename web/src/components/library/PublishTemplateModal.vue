<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { RouterLink } from 'vue-router';
import FmModal from '@/components/common/FmModal.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import TemplatePricingFields from '@/components/library/TemplatePricingFields.vue';
import { templateService } from '@/services/template.service';
import { publisherService } from '@/services/publisher.service';
import { toUserFacingError } from '@/utils/error.util';
import { draftToPricing, validatePricingDraft } from '@/utils/money.util';
import {
  OWNER_ONLY_PUBLISHER_MESSAGE,
  isForbiddenError,
  isPublisherNotReadyError,
} from '@/utils/marketplace.util';
import type { PricingDraft, PublisherStatus } from '@/types/marketplace';

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
const DEFAULT_FEE_PERCENT = 20;

const name = ref('');
const description = ref('');
const category = ref<(typeof CATEGORIES)[number]>('Operations');
const tagsInput = ref('');
const previewImageUrl = ref('');
const pricing = ref<PricingDraft>({ pricingModel: 'free', amount: '', currency: 'usd' });
const pricingError = ref<string | null>(null);

const submitting = ref(false);
const error = ref<string | null>(null);

// Publisher readiness (only fetched once the user picks a paid model).
const publisher = ref<PublisherStatus | null>(null);
const publisherLoading = ref(false);
const publisherError = ref<string | null>(null);
const publisherForbidden = ref(false);

const isPaidDraft = computed(() => pricing.value.pricingModel !== 'free');
const feePercent = computed(() => publisher.value?.platformFeePercent ?? DEFAULT_FEE_PERCENT);
const publisherBlocked = computed<boolean>(
  () =>
    isPaidDraft.value &&
    (publisherForbidden.value || (publisher.value !== null && !publisher.value.canSellPaidWorkflows)),
);

async function loadPublisher(): Promise<void> {
  if (publisher.value || publisherLoading.value) return;
  publisherLoading.value = true;
  publisherError.value = null;
  publisherForbidden.value = false;
  try {
    publisher.value = await publisherService.status();
  } catch (e) {
    if (isForbiddenError(e)) publisherForbidden.value = true;
    else publisherError.value = `Couldn't check your publisher account. ${toUserFacingError(e).message}`;
  } finally {
    publisherLoading.value = false;
  }
}

watch(
  () => pricing.value.pricingModel,
  (model) => {
    pricingError.value = null;
    if (model !== 'free') void loadPublisher();
  },
);

watch(
  () => props.modelValue,
  (open) => {
    if (open) {
      name.value = props.workflowName ?? '';
      description.value = '';
      category.value = 'Operations';
      tagsInput.value = '';
      previewImageUrl.value = '';
      pricing.value = { pricingModel: 'free', amount: '', currency: 'usd' };
      pricingError.value = null;
      publisher.value = null;
      publisherError.value = null;
      publisherForbidden.value = false;
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
  pricingError.value = validatePricingDraft(pricing.value);
  if (pricingError.value) return;
  if (publisherBlocked.value) {
    error.value = 'Finish publisher setup before publishing a paid template, or switch pricing to Free.';
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
      ...draftToPricing(pricing.value),
    });
    emit('published');
    emit('update:modelValue', false);
  } catch (err: unknown) {
    if (isPublisherNotReadyError(err)) {
      // The backend is the source of truth — refresh readiness so the explanation shows.
      publisher.value = null;
      await loadPublisher();
      error.value =
        'Your publisher account can\'t accept payments yet. Finish setup in Settings → Publisher, or publish as Free.';
    } else if (isForbiddenError(err) && isPaidDraft.value) {
      error.value = OWNER_ONLY_PUBLISHER_MESSAGE;
    } else {
      error.value = `Could not publish the template. ${toUserFacingError(err).message} Make sure the workflow is published, then try again.`;
    }
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
        Share this workflow with the community — free, or as a paid workflow. It will appear in the
        template gallery after review.
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

      <!-- Pricing (Workflow Economy §8.9) -->
      <div class="space-y-3 rounded-lg border border-slate-200 bg-slate-50/60 p-3">
        <div>
          <p class="text-sm font-medium text-slate-700">
            Pricing
          </p>
          <p class="text-xs text-slate-500">
            Sell this workflow once or as a monthly subscription. Flowamaz keeps {{ feePercent }}%, you receive the rest.
          </p>
        </div>
        <TemplatePricingFields
          v-model="pricing"
          :fee-percent="feePercent"
          :disabled="submitting"
          :error="pricingError"
        />

        <template v-if="isPaidDraft">
          <p
            v-if="publisherLoading"
            class="flex items-center gap-2 text-xs text-slate-500"
          >
            <FmSpinner size="sm" />
            Checking your publisher account…
          </p>
          <FmAlert
            v-else-if="publisherForbidden"
            type="warning"
          >
            {{ OWNER_ONLY_PUBLISHER_MESSAGE }}
          </FmAlert>
          <FmAlert
            v-else-if="publisherError"
            type="error"
          >
            <p>{{ publisherError }}</p>
            <button
              type="button"
              class="mt-1 text-xs font-medium underline"
              @click="loadPublisher"
            >
              Check again
            </button>
          </FmAlert>
          <FmAlert
            v-else-if="publisher && !publisher.canSellPaidWorkflows"
            type="warning"
          >
            <p class="font-medium">
              Set up payouts before selling
            </p>
            <p class="mt-1 text-xs">
              {{ publisher.connected
                ? 'Your Stripe publisher account still needs details before it can accept payments.'
                : 'Paid workflows pay out through Stripe. Connect a publisher account first — it takes about 5 minutes.' }}
              You can also publish as Free now and add a price later.
            </p>
            <RouterLink
              :to="{ name: 'publisher' }"
              class="mt-2 inline-block text-xs font-semibold underline"
              @click="close"
            >
              Open Publisher settings →
            </RouterLink>
          </FmAlert>
        </template>
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
        :disabled="submitting || (isPaidDraft && (publisherLoading || publisherBlocked))"
        class="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-700 disabled:opacity-50"
        @click="onPublish"
      >
        {{ submitting ? 'Publishing…' : 'Publish template' }}
      </button>
    </template>
  </FmModal>
</template>
