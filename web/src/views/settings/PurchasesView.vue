<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { ShoppingBag, XCircle } from 'lucide-vue-next';
import FmButton from '@/components/common/FmButton.vue';
import FmBadge from '@/components/common/FmBadge.vue';
import FmModal from '@/components/common/FmModal.vue';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmEmptyState from '@/components/common/FmEmptyState.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import { marketplaceService } from '@/services/marketplace.service';
import { useWorkspace } from '@/composables/useWorkspace';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { formatDate } from '@/utils/date.util';
import { formatPrice } from '@/utils/money.util';
import {
  PURCHASE_STATUS_LABELS,
  PURCHASE_STATUS_VARIANT,
  canCancelPurchase,
  isForbiddenError,
} from '@/utils/marketplace.util';
import type { TemplatePurchase } from '@/types/marketplace';

const router = useRouter();
const toast = useToast();
const { currentWorkspaceId, current, loadWorkspaces } = useWorkspace();

const loading = ref(true);
const loadError = ref('');
const purchases = ref<TemplatePurchase[]>([]);

const confirmTarget = ref<TemplatePurchase | null>(null);
const confirmOpen = ref(false);
const canceling = ref(false);
const cancelError = ref('');

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = '';
  try {
    if (!currentWorkspaceId.value) await loadWorkspaces();
    const workspaceId = currentWorkspaceId.value;
    if (!workspaceId) {
      loadError.value = 'No workspace is selected. Pick a workspace in the sidebar to see what it has bought.';
      return;
    }
    purchases.value = await marketplaceService.listPurchases(workspaceId);
  } catch (e) {
    loadError.value = toUserFacingError(e).message;
  } finally {
    loading.value = false;
  }
}

function renewalText(p: TemplatePurchase): string {
  if (p.pricingModel !== 'monthly') return 'Lifetime access';
  if (!p.currentPeriodEnd) return '—';
  if (p.cancelAtPeriodEnd || p.status === 'canceled') return `Ends ${formatDate(p.currentPeriodEnd)}`;
  return `Renews ${formatDate(p.currentPeriodEnd)}`;
}

function askCancel(p: TemplatePurchase): void {
  confirmTarget.value = p;
  cancelError.value = '';
  confirmOpen.value = true;
}

async function confirmCancel(): Promise<void> {
  const target = confirmTarget.value;
  const workspaceId = currentWorkspaceId.value;
  if (!target || !workspaceId) return;
  canceling.value = true;
  cancelError.value = '';
  try {
    const updated = await marketplaceService.cancelPurchase(workspaceId, target.id);
    purchases.value = purchases.value.map((p) => (p.id === updated.id ? updated : p));
    confirmOpen.value = false;
    toast.success(
      updated.currentPeriodEnd
        ? `Subscription canceled. ${updated.templateName} stays unlocked until ${formatDate(updated.currentPeriodEnd)}.`
        : `Subscription to ${updated.templateName} canceled.`,
    );
  } catch (e) {
    cancelError.value = isForbiddenError(e)
      ? 'Only your organisation owner can cancel subscriptions. Ask your org owner to cancel it from this page.'
      : `Couldn't cancel the subscription. ${toUserFacingError(e).message}`;
  } finally {
    canceling.value = false;
  }
}

onMounted(load);
watch(currentWorkspaceId, (next, prev) => {
  if (next && next !== prev) void load();
});
</script>

<template>
  <div class="mx-auto max-w-4xl space-y-6 p-6">
    <header>
      <h1 class="text-2xl font-semibold text-slate-900">
        Purchases
      </h1>
      <p class="text-sm text-slate-500">
        Paid workflow templates bought for {{ current?.name ?? 'this workspace' }}, and their subscriptions.
      </p>
    </header>

    <div
      v-if="loading"
      class="flex justify-center py-16 text-primary-600"
    >
      <FmSpinner size="lg" />
    </div>

    <FmErrorState
      v-else-if="loadError"
      title="Couldn't load purchases"
      :description="loadError"
      action-label="Try again"
      @action="load"
    />

    <FmEmptyState
      v-else-if="purchases.length === 0"
      :icon="ShoppingBag"
      title="This workspace hasn't bought any templates yet"
      description="Paid templates in the gallery show a price badge. Open one and choose Buy or Subscribe — once purchased, it's listed here and anyone in the workspace can install it."
      cta-label="Browse templates"
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
              Template
            </th>
            <th class="px-4 py-2 font-medium">
              Price
            </th>
            <th class="px-4 py-2 font-medium">
              Status
            </th>
            <th class="px-4 py-2 font-medium">
              Purchased
            </th>
            <th class="px-4 py-2 font-medium">
              Access
            </th>
            <th class="px-4 py-2" />
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100">
          <tr
            v-for="p in purchases"
            :key="p.id"
          >
            <td class="px-4 py-3 font-medium text-slate-900">
              <RouterLink
                :to="{ name: 'template-detail', params: { id: p.templateId } }"
                class="hover:text-primary-700 hover:underline"
              >
                {{ p.templateName }}
              </RouterLink>
            </td>
            <td class="whitespace-nowrap px-4 py-3 text-slate-600">
              {{ formatPrice(p.pricingModel, p.amountCents, p.currency) }}
            </td>
            <td class="px-4 py-3">
              <FmBadge :variant="PURCHASE_STATUS_VARIANT[p.status] ?? 'slate'">
                {{ p.cancelAtPeriodEnd && p.status === 'active' ? 'Canceling' : (PURCHASE_STATUS_LABELS[p.status] ?? p.status) }}
              </FmBadge>
            </td>
            <td class="whitespace-nowrap px-4 py-3 text-slate-600">
              {{ formatDate(p.purchasedAt) }}
            </td>
            <td class="whitespace-nowrap px-4 py-3 text-slate-600">
              {{ renewalText(p) }}
            </td>
            <td class="px-4 py-3 text-right">
              <button
                v-if="canCancelPurchase(p)"
                type="button"
                class="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs font-medium text-danger-600 hover:bg-danger-50"
                @click="askCancel(p)"
              >
                <XCircle class="h-3.5 w-3.5" />
                Cancel subscription
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <FmModal
      v-model="confirmOpen"
      size="sm"
      title="Cancel subscription?"
    >
      <div class="space-y-3 text-sm text-slate-600">
        <p v-if="confirmTarget">
          <strong class="text-slate-900">{{ confirmTarget.templateName }}</strong> stays unlocked until the end of the
          current billing period{{ confirmTarget.currentPeriodEnd ? ` (${formatDate(confirmTarget.currentPeriodEnd)})` : '' }}.
          After that, Install is locked for this workspace until someone subscribes again.
        </p>
        <p
          v-if="cancelError"
          class="text-danger-600"
        >
          {{ cancelError }}
        </p>
      </div>
      <template #footer>
        <FmButton
          variant="secondary"
          :disabled="canceling"
          @click="confirmOpen = false"
        >
          Keep subscription
        </FmButton>
        <FmButton
          variant="danger"
          :loading="canceling"
          @click="confirmCancel"
        >
          Cancel at period end
        </FmButton>
      </template>
    </FmModal>
  </div>
</template>
