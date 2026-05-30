<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import { Check, Mail } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import { useAuth } from '@/composables/useAuth';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { billingService, type BillingPlan } from '@/services/billing.service';

const router = useRouter();
const auth = useAuth();
const toast = useToast();

const loading = ref(true);
const loadError = ref('');
const annual = ref(false);
const plans = ref<BillingPlan[]>([]);
const currentPlanSlug = ref<string | null>(null);
const upgrading = ref<string | null>(null);

const ANNUAL_DISCOUNT_LABEL = 'Save ~20%';

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = '';
  try {
    plans.value = await billingService.plans();
    if (auth.isAuthenticated.value) {
      try {
        const current = await billingService.current();
        currentPlanSlug.value = current.planSlug;
      } catch {
        // Pricing is public — a failed current-plan lookup must not break the page.
        currentPlanSlug.value = null;
      }
    }
  } catch (e) {
    loadError.value = toUserFacingError(e).message;
  } finally {
    loading.value = false;
  }
}

onMounted(load);

function priceLabel(plan: BillingPlan): string {
  if (plan.slug === 'community') return 'Free';
  if (plan.slug === 'enterprise') return 'Custom';
  const monthly = annual.value ? plan.priceAnnualUsd / 12 : plan.priceMonthlyUsd;
  return `$${Math.round(monthly)}`;
}

function limitLabel(value: number, unit: string): string {
  return value <= 0 ? `Unlimited ${unit}` : `${value.toLocaleString()} ${unit}`;
}

function isCurrent(plan: BillingPlan): boolean {
  return currentPlanSlug.value === plan.slug;
}

async function choose(plan: BillingPlan): Promise<void> {
  if (plan.slug === 'enterprise') {
    window.location.href = 'mailto:sales@flowamaz.io?subject=Flowamaz%20Enterprise%20enquiry';
    return;
  }
  if (plan.slug === 'community' || isCurrent(plan)) return;

  if (!auth.isAuthenticated.value) {
    router.push({ path: '/login', query: { redirect: '/pricing' } });
    return;
  }

  upgrading.value = plan.slug;
  try {
    const origin = window.location.origin;
    const url = await billingService.checkout({
      planId: plan.id,
      annual: annual.value,
      successUrl: `${origin}/settings/billing?checkout=success`,
      cancelUrl: `${origin}/pricing`,
    });
    window.location.href = url;
  } catch (e) {
    toast.error(toUserFacingError(e).message);
    upgrading.value = null;
  }
}

function ctaLabel(plan: BillingPlan): string {
  if (plan.slug === 'enterprise') return 'Contact sales';
  if (isCurrent(plan)) return 'Current plan';
  if (plan.slug === 'community') return 'Get started';
  return 'Upgrade';
}
</script>

<template>
  <div class="min-h-screen bg-slate-50 px-4 py-12">
    <div class="mx-auto max-w-6xl">
      <header class="mb-10 text-center">
        <h1 class="text-3xl font-bold text-slate-900">
          Plans that scale with your workflows
        </h1>
        <p class="mt-2 text-slate-500">
          Start free. Upgrade when you need more workflows, runs and teammates.
        </p>

        <div class="mt-6 inline-flex items-center gap-3 rounded-full border border-slate-200 bg-white p-1">
          <button
            type="button"
            :class="[
              'rounded-full px-4 py-1.5 text-sm font-medium',
              !annual ? 'bg-primary-600 text-white' : 'text-slate-600',
            ]"
            @click="annual = false"
          >
            Monthly
          </button>
          <button
            type="button"
            :class="[
              'flex items-center gap-2 rounded-full px-4 py-1.5 text-sm font-medium',
              annual ? 'bg-primary-600 text-white' : 'text-slate-600',
            ]"
            @click="annual = true"
          >
            Annual
            <span
              :class="[
                'rounded-full px-2 py-0.5 text-xs',
                annual ? 'bg-white/20 text-white' : 'bg-accent-100 text-accent-700',
              ]"
            >{{ ANNUAL_DISCOUNT_LABEL }}</span>
          </button>
        </div>
      </header>

      <div
        v-if="loading"
        class="flex justify-center py-20 text-primary-600"
      >
        <FmSpinner size="lg" />
      </div>

      <FmErrorState
        v-else-if="loadError"
        title="Couldn't load pricing"
        :description="loadError"
        action-label="Try again"
        @action="load"
      />

      <div
        v-else
        class="grid gap-6 md:grid-cols-2 lg:grid-cols-4"
      >
        <div
          v-for="plan in plans"
          :key="plan.id"
          :class="[
            'flex flex-col rounded-2xl border bg-white p-6 shadow-sm',
            isCurrent(plan) ? 'border-primary-500 ring-2 ring-primary-200' : 'border-slate-200',
          ]"
        >
          <div class="flex items-center justify-between">
            <h2 class="text-lg font-semibold text-slate-900">
              {{ plan.name }}
            </h2>
            <FmBadge
              v-if="isCurrent(plan)"
              variant="primary"
            >
              Current
            </FmBadge>
          </div>

          <div class="mt-4 flex items-baseline gap-1">
            <span class="text-3xl font-bold text-slate-900">{{ priceLabel(plan) }}</span>
            <span
              v-if="plan.slug !== 'community' && plan.slug !== 'enterprise'"
              class="text-sm text-slate-500"
            >/mo</span>
          </div>
          <p
            v-if="annual && plan.slug !== 'community' && plan.slug !== 'enterprise'"
            class="mt-1 text-xs text-slate-400"
          >
            Billed ${{ Math.round(plan.priceAnnualUsd).toLocaleString() }} per year
          </p>

          <ul class="mt-6 space-y-3 text-sm text-slate-600">
            <li class="flex items-start gap-2">
              <Check class="mt-0.5 h-4 w-4 shrink-0 text-primary-600" />
              {{ limitLabel(plan.maxWorkflows, 'workflows') }}
            </li>
            <li class="flex items-start gap-2">
              <Check class="mt-0.5 h-4 w-4 shrink-0 text-primary-600" />
              {{ limitLabel(plan.maxRunsMonth, 'runs / month') }}
            </li>
            <li class="flex items-start gap-2">
              <Check class="mt-0.5 h-4 w-4 shrink-0 text-primary-600" />
              {{ limitLabel(plan.maxMembers, 'members') }}
            </li>
            <li
              v-for="feature in plan.features"
              :key="feature"
              class="flex items-start gap-2"
            >
              <Check class="mt-0.5 h-4 w-4 shrink-0 text-primary-600" />
              {{ feature }}
            </li>
          </ul>

          <div class="mt-6 grow" />

          <FmButton
            :variant="isCurrent(plan) ? 'secondary' : 'primary'"
            :disabled="isCurrent(plan)"
            :loading="upgrading === plan.slug"
            block
            @click="choose(plan)"
          >
            <template
              v-if="plan.slug === 'enterprise'"
              #icon-left
            >
              <Mail class="h-4 w-4" />
            </template>
            {{ ctaLabel(plan) }}
          </FmButton>
        </div>
      </div>
    </div>
  </div>
</template>
