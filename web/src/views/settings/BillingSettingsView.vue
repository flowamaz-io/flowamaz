<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import { CreditCard, ArrowUpRight, ExternalLink } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import {
  billingService,
  type CurrentSubscription,
  type BillingUsage,
} from '@/services/billing.service';

const router = useRouter();
const toast = useToast();

const loading = ref(true);
const loadError = ref('');
const opening = ref(false);
const subscription = ref<CurrentSubscription | null>(null);
const usage = ref<BillingUsage | null>(null);

const statusBadge = computed<{ label: string; variant: 'primary' | 'amber' | 'danger' | 'slate' }>(() => {
  const status = subscription.value?.status ?? 'community';
  if (subscription.value?.paymentFailed) return { label: 'Past due', variant: 'danger' };
  switch (status) {
    case 'active':
      return { label: 'Active', variant: 'primary' };
    case 'trialing':
      return { label: 'Trial', variant: 'amber' };
    case 'pastdue':
    case 'past_due':
      return { label: 'Past due', variant: 'danger' };
    case 'cancelled':
    case 'canceled':
      return { label: 'Cancelled', variant: 'slate' };
    default:
      return { label: 'Active', variant: 'primary' };
  }
});

const trialMessage = computed(() => {
  const days = subscription.value?.trialDaysRemaining;
  if (days === null || days === undefined) return '';
  if (days <= 0) return 'Your trial has ended. Upgrade to keep your team running.';
  return `${days} ${days === 1 ? 'day' : 'days'} remaining in your trial`;
});

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = '';
  try {
    const [sub, use] = await Promise.all([billingService.current(), billingService.usage()]);
    subscription.value = sub;
    usage.value = use;
  } catch (e) {
    loadError.value = toUserFacingError(e).message;
  } finally {
    loading.value = false;
  }
}

onMounted(load);

function usagePercent(used: number, limit: number): number {
  if (limit <= 0) return 0; // unlimited
  return Math.min(100, Math.round((used / limit) * 100));
}

function limitText(used: number, limit: number, unit: string): string {
  if (limit <= 0) return `${used.toLocaleString()} ${unit} (unlimited)`;
  return `${used.toLocaleString()} / ${limit.toLocaleString()} ${unit}`;
}

async function manageBilling(): Promise<void> {
  opening.value = true;
  try {
    const url = await billingService.portal(`${window.location.origin}/settings/billing`);
    window.location.href = url;
  } catch (e) {
    toast.error(toUserFacingError(e).message);
    opening.value = false;
  }
}

function upgrade(): void {
  router.push('/pricing');
}
</script>

<template>
  <div class="mx-auto max-w-3xl space-y-6 p-6">
    <header class="flex items-center justify-between">
      <div>
        <h1 class="text-2xl font-semibold text-slate-900">
          Billing
        </h1>
        <p class="text-sm text-slate-500">
          Manage your plan, payment method and usage.
        </p>
      </div>
      <FmBadge :variant="statusBadge.variant">
        {{ statusBadge.label }}
      </FmBadge>
    </header>

    <div
      v-if="loading"
      class="flex justify-center py-16 text-primary-600"
    >
      <FmSpinner size="lg" />
    </div>

    <FmErrorState
      v-else-if="loadError"
      title="Couldn't load billing"
      :description="loadError"
      action-label="Try again"
      @action="load"
    />

    <template v-else>
      <FmAlert
        v-if="subscription?.paymentFailed"
        type="error"
      >
        <p class="text-sm font-medium">
          Your last payment failed
        </p>
        <p class="mt-1 text-xs">
          Update your payment method in the billing portal to restore full access.
        </p>
      </FmAlert>

      <FmAlert
        v-else-if="trialMessage"
        type="info"
      >
        <p class="text-sm font-medium">
          {{ trialMessage }}
        </p>
      </FmAlert>

      <section class="rounded-xl border border-slate-200 bg-white p-6">
        <div class="flex items-start justify-between">
          <div>
            <p class="text-sm text-slate-500">
              Current plan
            </p>
            <p class="text-xl font-semibold text-slate-900">
              {{ subscription?.planName ?? 'Community' }}
            </p>
            <p
              v-if="subscription?.currentPeriodEnd"
              class="mt-1 text-xs text-slate-400"
            >
              Renews {{ new Date(subscription.currentPeriodEnd).toLocaleDateString() }}
              ({{ subscription.billingCycle }})
            </p>
          </div>
          <div class="flex gap-2">
            <FmButton
              variant="secondary"
              :loading="opening"
              :disabled="!subscription?.hasBillingAccount"
              @click="manageBilling"
            >
              <template #icon-left>
                <CreditCard class="h-4 w-4" />
              </template>
              Manage billing
            </FmButton>
            <FmButton @click="upgrade">
              <template #icon-left>
                <ArrowUpRight class="h-4 w-4" />
              </template>
              Upgrade plan
            </FmButton>
          </div>
        </div>
      </section>

      <section
        v-if="usage"
        class="space-y-4 rounded-xl border border-slate-200 bg-white p-6"
      >
        <h2 class="text-sm font-medium text-slate-700">
          Usage this period ({{ usage.period }})
        </h2>

        <div>
          <div class="mb-1 flex justify-between text-xs text-slate-500">
            <span>Runs</span>
            <span>{{ limitText(usage.runsUsed, usage.runsLimit, 'runs') }}</span>
          </div>
          <div class="h-2 overflow-hidden rounded-full bg-slate-100">
            <div
              class="h-full rounded-full bg-primary-500"
              :style="{ width: `${usagePercent(usage.runsUsed, usage.runsLimit)}%` }"
            />
          </div>
        </div>

        <div>
          <div class="mb-1 flex justify-between text-xs text-slate-500">
            <span>Members</span>
            <span>{{ limitText(usage.membersUsed, usage.membersLimit, 'members') }}</span>
          </div>
          <div class="h-2 overflow-hidden rounded-full bg-slate-100">
            <div
              class="h-full rounded-full bg-accent-500"
              :style="{ width: `${usagePercent(usage.membersUsed, usage.membersLimit)}%` }"
            />
          </div>
        </div>

        <div>
          <div class="mb-1 flex justify-between text-xs text-slate-500">
            <span>AI calls</span>
            <span>{{ usage.aiCallsUsed.toLocaleString() }} (${{ usage.aiCostUsd.toFixed(2) }})</span>
          </div>
          <div class="h-2 overflow-hidden rounded-full bg-slate-100">
            <div
              class="h-full rounded-full bg-amber-400"
              :style="{ width: `${usage.aiCallsUsed > 0 ? 100 : 0}%` }"
            />
          </div>
        </div>
      </section>

      <section class="rounded-xl border border-slate-200 bg-white p-6">
        <h2 class="text-sm font-medium text-slate-700">
          Invoice history
        </h2>
        <p class="mt-2 text-sm text-slate-500">
          Your last 6 months of invoices live in the Stripe billing portal — open it to view or
          download any invoice.
        </p>
        <FmButton
          class="mt-3"
          variant="secondary"
          :loading="opening"
          :disabled="!subscription?.hasBillingAccount"
          @click="manageBilling"
        >
          <template #icon-left>
            <ExternalLink class="h-4 w-4" />
          </template>
          Open invoice history
        </FmButton>
      </section>
    </template>
  </div>
</template>
