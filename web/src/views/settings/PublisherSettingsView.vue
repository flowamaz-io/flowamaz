<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import {
  BadgeCheck, CircleDollarSign, ExternalLink, Pencil, Receipt, RefreshCw, Repeat, ShieldAlert, Store,
} from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import TemplatePriceBadge from '@/components/library/TemplatePriceBadge.vue';
import TemplatePricingFields from '@/components/library/TemplatePricingFields.vue';
import StripeTestModeHint from '@/components/library/StripeTestModeHint.vue';
import { publisherService } from '@/services/publisher.service';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { formatDate } from '@/utils/date.util';
import { draftToPricing, formatMoney, pricingToDraft, validatePricingDraft } from '@/utils/money.util';
import {
  OWNER_ONLY_PUBLISHER_MESSAGE,
  humanizeRequirement,
  isForbiddenError,
  publisherOnboardingState,
} from '@/utils/marketplace.util';
import type {
  LedgerEntryType,
  PricingDraft,
  PublisherEarnings,
  PublisherListing,
  PublisherStatus,
} from '@/types/marketplace';

const route = useRoute();
const router = useRouter();
const toast = useToast();

const loading = ref(true);
const loadError = ref('');
const forbidden = ref(false);
const status = ref<PublisherStatus | null>(null);

const earnings = ref<PublisherEarnings | null>(null);
const earningsError = ref('');

const onboarding = ref(false);
const onboardingError = ref('');
const openingDashboard = ref(false);
/** `?onboarding=refresh` — Stripe's link expired and must be regenerated. */
const linkExpired = ref(false);

const state = computed(() => (status.value ? publisherOnboardingState(status.value) : 'not_connected'));
const feePercent = computed(() => status.value?.platformFeePercent ?? 20);

const stateBadge = computed<{ label: string; variant: 'primary' | 'amber' | 'slate' | 'danger' }>(() => {
  switch (state.value) {
    case 'ready':
      return { label: 'Ready to sell', variant: 'primary' };
    case 'requirements_due':
      return { label: 'Action needed', variant: 'danger' };
    case 'incomplete':
      return { label: 'Setup incomplete', variant: 'amber' };
    default:
      return { label: 'Not connected', variant: 'slate' };
  }
});

const currency = computed(() => earnings.value?.currency ?? 'usd');

const tiles = computed(() => {
  const e = earnings.value;
  if (!e) return [];
  return [
    { label: 'Gross sales', value: formatMoney(e.grossCents, e.currency), icon: CircleDollarSign },
    { label: `Flowamaz fee (${feePercent.value}%)`, value: formatMoney(e.feeCents, e.currency), icon: Receipt },
    { label: 'Your net earnings', value: formatMoney(e.netCents, e.currency), icon: BadgeCheck },
    { label: 'Sales', value: e.salesCount.toLocaleString(), icon: Store },
    { label: 'Active subscriptions', value: e.activeSubscriptions.toLocaleString(), icon: Repeat },
  ];
});

const ENTRY_LABELS: Record<LedgerEntryType, { label: string; variant: 'primary' | 'blue' | 'amber' }> = {
  sale: { label: 'Sale', variant: 'primary' },
  renewal: { label: 'Renewal', variant: 'blue' },
  refund: { label: 'Refund', variant: 'amber' },
};

async function loadEarnings(): Promise<void> {
  earningsError.value = '';
  try {
    earnings.value = await publisherService.earnings();
  } catch (e) {
    earningsError.value = `Couldn't load your earnings. ${toUserFacingError(e).message}`;
  }
}

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = '';
  forbidden.value = false;
  try {
    status.value = await publisherService.status();
    if (status.value.connected) await loadEarnings();
  } catch (e) {
    if (isForbiddenError(e)) forbidden.value = true;
    else loadError.value = toUserFacingError(e).message;
  } finally {
    loading.value = false;
  }
}

async function startOnboarding(): Promise<void> {
  onboarding.value = true;
  onboardingError.value = '';
  const base = `${window.location.origin}/settings/publisher`;
  try {
    const url = await publisherService.onboarding({
      returnUrl: `${base}?onboarding=return`,
      refreshUrl: `${base}?onboarding=refresh`,
    });
    window.location.href = url;
  } catch (e) {
    onboardingError.value = isForbiddenError(e)
      ? OWNER_ONLY_PUBLISHER_MESSAGE
      : `Couldn't open Stripe onboarding. ${toUserFacingError(e).message}`;
    onboarding.value = false;
  }
}

async function openDashboard(): Promise<void> {
  // Open the tab synchronously so pop-up blockers allow it, then point it at Stripe.
  const tab = window.open('', '_blank');
  openingDashboard.value = true;
  try {
    const url = await publisherService.dashboardLink();
    if (tab) tab.location.href = url;
    else window.open(url, '_blank', 'noopener');
  } catch (e) {
    tab?.close();
    toast.error(`Couldn't open the payouts dashboard. ${toUserFacingError(e).message}`);
  } finally {
    openingDashboard.value = false;
  }
}

// ── Inline listing pricing edit ──────────────────────────────────────────────
const editingId = ref<string | null>(null);
const draft = ref<PricingDraft>({ pricingModel: 'free', amount: '', currency: 'usd' });
const rowError = ref<string | null>(null);
const savingRow = ref(false);

function editListing(listing: PublisherListing): void {
  editingId.value = listing.templateId;
  draft.value = pricingToDraft(listing);
  rowError.value = null;
}

function cancelEdit(): void {
  editingId.value = null;
  rowError.value = null;
}

async function saveListing(listing: PublisherListing): Promise<void> {
  rowError.value = validatePricingDraft(draft.value);
  if (rowError.value) return;
  if (draft.value.pricingModel !== 'free' && !status.value?.canSellPaidWorkflows) {
    rowError.value = 'Finish Stripe setup above before setting a paid price, or keep this listing Free.';
    return;
  }
  savingRow.value = true;
  try {
    const updated = await publisherService.updateListingPricing(listing.templateId, draftToPricing(draft.value));
    if (earnings.value) {
      earnings.value.listings = earnings.value.listings.map((l) => (l.templateId === updated.templateId ? updated : l));
    }
    editingId.value = null;
    toast.success(`Pricing updated for ${updated.name}. Existing subscribers keep their current price.`);
  } catch (e) {
    rowError.value = `Couldn't update pricing. ${toUserFacingError(e).message}`;
  } finally {
    savingRow.value = false;
  }
}

function reviewVariant(reviewStatus: string): 'primary' | 'amber' | 'danger' | 'slate' {
  const s = reviewStatus.toLowerCase();
  if (s === 'approved' || s === 'published') return 'primary';
  if (s === 'rejected') return 'danger';
  if (s === 'pending' || s === 'in_review' || s === 'pendingreview') return 'amber';
  return 'slate';
}

onMounted(async () => {
  const flag = route.query.onboarding;
  if (flag === 'return' || flag === 'refresh') {
    linkExpired.value = flag === 'refresh';
    const query = { ...route.query };
    delete query.onboarding;
    void router.replace({ query });
  }
  await load();
  if (flag === 'return' && status.value) {
    if (status.value.canSellPaidWorkflows) toast.success('Stripe setup complete — you can now sell paid workflows.');
    else toast.info('Stripe saved your progress. Finish the remaining steps below to start selling.');
  }
});
</script>

<template>
  <div class="mx-auto max-w-5xl space-y-6 p-6">
    <header class="flex flex-wrap items-start justify-between gap-3">
      <div>
        <h1 class="text-2xl font-semibold text-slate-900">
          Publisher
        </h1>
        <p class="text-sm text-slate-500">
          Sell workflows in the template gallery. Flowamaz keeps {{ feePercent }}%, you receive {{ 100 - feePercent }}% — paid out by Stripe.
        </p>
      </div>
      <FmBadge
        v-if="status && !loading"
        :variant="stateBadge.variant"
      >
        {{ stateBadge.label }}
      </FmBadge>
    </header>

    <StripeTestModeHint />

    <div
      v-if="loading"
      class="flex justify-center py-16 text-primary-600"
    >
      <FmSpinner size="lg" />
    </div>

    <FmEmptyState
      v-else-if="forbidden"
      :icon="ShieldAlert"
      title="Publisher settings are for the organisation owner"
      :description="OWNER_ONLY_PUBLISHER_MESSAGE + ' You can still publish free templates from any workflow.'"
      cta-label="Go to workflows"
      @cta="router.push('/workflows')"
    />

    <FmErrorState
      v-else-if="loadError"
      title="Couldn't load your publisher account"
      :description="loadError"
      action-label="Try again"
      @action="load"
    />

    <template v-else-if="status">
      <!-- Onboarding status -->
      <section class="rounded-xl border border-slate-200 bg-white p-6">
        <FmAlert
          v-if="linkExpired && state !== 'ready'"
          type="info"
          class="mb-4"
        >
          Your Stripe setup link expired. Select <strong>Continue setup</strong> to get a fresh one — your progress is saved.
        </FmAlert>

        <div
          v-if="state === 'not_connected'"
          class="flex flex-wrap items-start justify-between gap-4"
        >
          <div class="max-w-xl">
            <h2 class="text-base font-semibold text-slate-900">
              Connect a Stripe account to get paid
            </h2>
            <p class="mt-1 text-sm text-slate-500">
              Stripe verifies your identity and bank details, then pays out your earnings automatically.
              It takes about 5 minutes. Free templates don't need this.
            </p>
          </div>
          <FmButton
            :loading="onboarding"
            @click="startOnboarding"
          >
            <template #icon-left>
              <ExternalLink class="h-4 w-4" />
            </template>
            Connect with Stripe
          </FmButton>
        </div>

        <div
          v-else-if="state === 'incomplete' || state === 'requirements_due'"
          class="space-y-4"
        >
          <div class="flex flex-wrap items-start justify-between gap-4">
            <div class="max-w-xl">
              <h2 class="text-base font-semibold text-slate-900">
                {{ state === 'requirements_due' ? 'Stripe needs a few more details' : 'Finish your Stripe setup' }}
              </h2>
              <p class="mt-1 text-sm text-slate-500">
                Paid workflows unlock once Stripe enables charges and payouts on your account.
              </p>
            </div>
            <FmButton
              :loading="onboarding"
              @click="startOnboarding"
            >
              <template #icon-left>
                <ExternalLink class="h-4 w-4" />
              </template>
              Continue setup
            </FmButton>
          </div>
          <ul class="grid gap-2 text-sm sm:grid-cols-3">
            <li class="flex items-center gap-2">
              <span :class="['h-2 w-2 rounded-full', status.detailsSubmitted ? 'bg-primary-500' : 'bg-slate-300']" />
              Details submitted
            </li>
            <li class="flex items-center gap-2">
              <span :class="['h-2 w-2 rounded-full', status.chargesEnabled ? 'bg-primary-500' : 'bg-slate-300']" />
              Charges enabled
            </li>
            <li class="flex items-center gap-2">
              <span :class="['h-2 w-2 rounded-full', status.payoutsEnabled ? 'bg-primary-500' : 'bg-slate-300']" />
              Payouts enabled
            </li>
          </ul>
          <div
            v-if="status.requirementsDue.length"
            class="rounded-lg border border-amber-200 bg-amber-50 p-3"
          >
            <p class="text-xs font-medium uppercase tracking-wide text-amber-800">
              Stripe is asking for
            </p>
            <ul class="mt-1 list-disc pl-5 text-sm text-amber-900">
              <li
                v-for="req in status.requirementsDue"
                :key="req"
              >
                {{ humanizeRequirement(req) }}
              </li>
            </ul>
          </div>
        </div>

        <div
          v-else
          class="flex flex-wrap items-start justify-between gap-4"
        >
          <div class="max-w-xl">
            <h2 class="flex items-center gap-2 text-base font-semibold text-slate-900">
              <BadgeCheck class="h-5 w-5 text-primary-600" />
              You're ready to sell paid workflows
            </h2>
            <p class="mt-1 text-sm text-slate-500">
              Charges and payouts are enabled{{ status.country ? ` (${status.country.toUpperCase()})` : '' }}.
              View payouts, balances and bank details in your Stripe dashboard.
            </p>
          </div>
          <FmButton
            variant="secondary"
            :loading="openingDashboard"
            @click="openDashboard"
          >
            <template #icon-left>
              <ExternalLink class="h-4 w-4" />
            </template>
            Open payouts dashboard
          </FmButton>
        </div>

        <p
          v-if="onboardingError"
          class="mt-3 text-sm text-danger-600"
        >
          {{ onboardingError }}
        </p>
      </section>

      <!-- Earnings -->
      <template v-if="status.connected">
        <FmErrorState
          v-if="earningsError"
          title="Couldn't load earnings"
          :description="earningsError"
          action-label="Try again"
          @action="loadEarnings"
        />

        <template v-else-if="earnings">
          <section class="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
            <div
              v-for="tile in tiles"
              :key="tile.label"
              class="rounded-xl border border-slate-200 bg-white p-4"
            >
              <div class="flex items-center gap-2 text-xs text-slate-500">
                <component
                  :is="tile.icon"
                  class="h-4 w-4 text-slate-400"
                />
                {{ tile.label }}
              </div>
              <p class="mt-2 text-xl font-semibold text-slate-900">
                {{ tile.value }}
              </p>
            </div>
          </section>

          <!-- Listings -->
          <section class="space-y-3">
            <h2 class="text-sm font-medium text-slate-700">
              Your listings
            </h2>
            <FmEmptyState
              v-if="earnings.listings.length === 0"
              :icon="Store"
              title="You haven't listed a workflow yet"
              description="Open any published workflow and choose “Publish as template”. Pick One-time or Monthly pricing to sell it — it appears here after you submit."
              cta-label="Choose a workflow"
              @cta="router.push('/workflows')"
            />
            <div
              v-else
              class="overflow-x-auto rounded-xl border border-slate-200 bg-white"
            >
              <table class="w-full text-left text-sm">
                <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                  <tr>
                    <th class="px-4 py-2 font-medium">
                      Workflow
                    </th>
                    <th class="px-4 py-2 font-medium">
                      Price
                    </th>
                    <th class="px-4 py-2 font-medium">
                      Review
                    </th>
                    <th class="px-4 py-2 text-right font-medium">
                      Installs
                    </th>
                    <th class="px-4 py-2 text-right font-medium">
                      Sales
                    </th>
                    <th class="px-4 py-2 text-right font-medium">
                      Net
                    </th>
                    <th class="px-4 py-2" />
                  </tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                  <tr
                    v-for="listing in earnings.listings"
                    :key="listing.templateId"
                    class="align-top"
                  >
                    <td class="px-4 py-3 font-medium text-slate-900">
                      <RouterLink
                        :to="{ name: 'template-detail', params: { id: listing.templateId } }"
                        class="hover:text-primary-700 hover:underline"
                      >
                        {{ listing.name }}
                      </RouterLink>
                    </td>
                    <td class="px-4 py-3">
                      <div
                        v-if="editingId === listing.templateId"
                        class="min-w-[18rem] space-y-2"
                      >
                        <TemplatePricingFields
                          v-model="draft"
                          compact
                          :fee-percent="feePercent"
                          :disabled="savingRow"
                          :error="rowError"
                        />
                        <div class="flex gap-2">
                          <FmButton
                            size="sm"
                            :loading="savingRow"
                            @click="saveListing(listing)"
                          >
                            Save
                          </FmButton>
                          <FmButton
                            size="sm"
                            variant="ghost"
                            :disabled="savingRow"
                            @click="cancelEdit"
                          >
                            Cancel
                          </FmButton>
                        </div>
                      </div>
                      <TemplatePriceBadge
                        v-else
                        :pricing-model="listing.pricingModel"
                        :price-cents="listing.priceCents"
                        :currency="listing.currency"
                      />
                    </td>
                    <td class="px-4 py-3">
                      <FmBadge :variant="reviewVariant(listing.reviewStatus)">
                        {{ listing.reviewStatus }}
                      </FmBadge>
                    </td>
                    <td class="px-4 py-3 text-right text-slate-600">
                      {{ listing.installCount.toLocaleString() }}
                    </td>
                    <td class="px-4 py-3 text-right text-slate-600">
                      {{ listing.salesCount.toLocaleString() }}
                    </td>
                    <td class="px-4 py-3 text-right font-medium text-slate-900">
                      {{ formatMoney(listing.netCents, listing.currency || currency) }}
                    </td>
                    <td class="px-4 py-3 text-right">
                      <button
                        v-if="editingId !== listing.templateId"
                        type="button"
                        class="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs font-medium text-slate-600 hover:bg-slate-100"
                        :aria-label="`Edit pricing for ${listing.name}`"
                        @click="editListing(listing)"
                      >
                        <Pencil class="h-3.5 w-3.5" />
                        Edit price
                      </button>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </section>

          <!-- Ledger -->
          <section class="space-y-3">
            <div class="flex items-center justify-between">
              <h2 class="text-sm font-medium text-slate-700">
                Earnings ledger
              </h2>
              <button
                type="button"
                class="inline-flex items-center gap-1 text-xs font-medium text-slate-500 hover:text-slate-700"
                @click="loadEarnings"
              >
                <RefreshCw class="h-3.5 w-3.5" />
                Refresh
              </button>
            </div>
            <FmEmptyState
              v-if="earnings.entries.length === 0"
              :icon="Receipt"
              title="No sales yet"
              description="Every sale, renewal and refund lands here with the Flowamaz fee and your net amount. Share your listing link so buyers can find it in the template gallery."
              cta-label="Open template gallery"
              @cta="router.push({ name: 'library', query: { tab: 'templates' } })"
            />
            <div
              v-else
              class="overflow-x-auto rounded-xl border border-slate-200 bg-white"
            >
              <table class="w-full text-left text-sm">
                <thead class="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                  <tr>
                    <th class="px-4 py-2 font-medium">
                      Date
                    </th>
                    <th class="px-4 py-2 font-medium">
                      Workflow
                    </th>
                    <th class="px-4 py-2 font-medium">
                      Type
                    </th>
                    <th class="px-4 py-2 text-right font-medium">
                      Gross
                    </th>
                    <th class="px-4 py-2 text-right font-medium">
                      Fee
                    </th>
                    <th class="px-4 py-2 text-right font-medium">
                      Net
                    </th>
                  </tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                  <tr
                    v-for="entry in earnings.entries"
                    :key="entry.id"
                  >
                    <td class="whitespace-nowrap px-4 py-3 text-slate-600">
                      {{ formatDate(entry.occurredAt) }}
                    </td>
                    <td class="px-4 py-3 text-slate-900">
                      {{ entry.templateName }}
                    </td>
                    <td class="px-4 py-3">
                      <FmBadge :variant="ENTRY_LABELS[entry.type]?.variant ?? 'slate'">
                        {{ ENTRY_LABELS[entry.type]?.label ?? entry.type }}
                      </FmBadge>
                    </td>
                    <td class="px-4 py-3 text-right text-slate-600">
                      {{ formatMoney(entry.grossCents, entry.currency) }}
                    </td>
                    <td class="px-4 py-3 text-right text-slate-500">
                      {{ formatMoney(entry.feeCents, entry.currency) }}
                    </td>
                    <td
                      :class="[
                        'px-4 py-3 text-right font-medium',
                        entry.netCents < 0 ? 'text-danger-600' : 'text-slate-900',
                      ]"
                    >
                      {{ formatMoney(entry.netCents, entry.currency) }}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </section>
        </template>
      </template>
    </template>
  </div>
</template>
