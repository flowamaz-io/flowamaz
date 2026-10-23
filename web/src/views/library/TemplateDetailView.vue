<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ArrowLeft, Download, Lock, RefreshCw, ShoppingCart } from 'lucide-vue-next';
import FmSpinner from '@/components/common/FmSpinner.vue';
import FmErrorState from '@/components/common/FmErrorState.vue';
import FmAlert from '@/components/common/FmAlert.vue';
import InstallTemplateModal from '@/components/library/InstallTemplateModal.vue';
import TemplatePriceBadge from '@/components/library/TemplatePriceBadge.vue';
import StripeTestModeHint from '@/components/library/StripeTestModeHint.vue';
import { templateService, type TemplateDetail } from '@/services/template.service';
import { marketplaceService } from '@/services/marketplace.service';
import { useWorkspace } from '@/composables/useWorkspace';
import { useTemplateAccess } from '@/composables/useTemplateAccess';
import { useToast } from '@/composables/useToast';
import { toUserFacingError } from '@/utils/error.util';
import { formatDate } from '@/utils/date.util';
import { formatMoney, isPaid } from '@/utils/money.util';
import {
  OWNER_ONLY_PURCHASE_MESSAGE,
  isForbiddenError,
  isPurchaseRequiredError,
} from '@/utils/marketplace.util';

const route = useRoute();
const router = useRouter();
const toast = useToast();
const { currentWorkspaceId, loadWorkspaces } = useWorkspace();
const {
  access,
  locked,
  polling,
  loading: accessLoading,
  error: accessError,
  load: loadTemplateAccess,
  pollUntilOwned,
} = useTemplateAccess();

const templateId = route.params.id as string;
const template = ref<TemplateDetail | null>(null);
const loading = ref(false);
const error = ref<string | null>(null);

const installModalOpen = ref(false);
const installModal = ref<InstanceType<typeof InstallTemplateModal> | null>(null);

const purchasing = ref(false);
const purchaseError = ref<string | null>(null);
/** Banner shown after Stripe Checkout redirects back. */
const checkoutReturn = ref<'success' | 'canceled' | null>(null);

/** Set when the gallery's Install button sent the user here because the template must be bought first. */
const cameToInstall = computed<boolean>(() => route.query.intent === 'install');

const paid = computed<boolean>(
  () => !!template.value && isPaid(template.value.pricingModel, template.value.priceCents),
);

const nodeSummaryLabel = computed<string>(() => {
  if (!template.value || template.value.nodeSummary.length === 0) return '';
  return template.value.nodeSummary.map((s) => `${s.count}× ${s.nodeType}`).join(', ');
});

const buyLabel = computed<string>(() => {
  const t = template.value;
  if (!t) return 'Buy';
  const amount = formatMoney(t.priceCents, t.currency);
  return t.pricingModel === 'monthly' ? `Subscribe ${amount}/mo` : `Buy for ${amount}`;
});

/** Licence note under the install button. */
const accessNote = computed<{ type: 'info' | 'warning' | 'success'; text: string } | null>(() => {
  const a = access.value;
  if (!paid.value || !a) return null;
  if (a.status === 'pending') {
    return {
      type: 'info',
      text: 'Payment is processing. Install unlocks automatically once Stripe confirms it — usually within a minute.',
    };
  }
  if (a.status === 'past_due') {
    return {
      type: 'warning',
      text: 'The latest subscription payment failed. Ask your org owner to update the payment method so this workspace keeps access.',
    };
  }
  if (a.owned && a.cancelAtPeriodEnd && a.currentPeriodEnd) {
    return { type: 'info', text: `Subscription canceled — access continues until ${formatDate(a.currentPeriodEnd)}.` };
  }
  if (a.owned && template.value?.pricingModel === 'monthly' && a.currentPeriodEnd) {
    return { type: 'success', text: `Subscribed — renews ${formatDate(a.currentPeriodEnd)}.` };
  }
  if (a.owned) return { type: 'success', text: 'Owned by this workspace — install as many copies as you need.' };
  return null;
});

async function resolveWorkspaceId(): Promise<string | null> {
  if (!currentWorkspaceId.value) await loadWorkspaces();
  return currentWorkspaceId.value ?? null;
}

async function loadAccess(): Promise<void> {
  if (!paid.value) return;
  const workspaceId = await resolveWorkspaceId();
  if (!workspaceId) return;
  await loadTemplateAccess(workspaceId, templateId);
}

async function load(): Promise<void> {
  loading.value = true;
  error.value = null;
  try {
    template.value = await templateService.get(templateId);
  } catch (err: unknown) {
    error.value = `${toUserFacingError(err).message} The template may have been removed — browse the gallery for current templates.`;
  } finally {
    loading.value = false;
  }
  if (template.value) await loadAccess();
}

/** Handles `?purchase=success|canceled` after Stripe Checkout. */
async function handleCheckoutReturn(): Promise<void> {
  const flag = route.query.purchase;
  if (flag !== 'success' && flag !== 'canceled') return;
  checkoutReturn.value = flag;
  // Drop the flag so a reload doesn't replay the banner.
  const query = { ...route.query };
  delete query.purchase;
  void router.replace({ query });
  if (flag === 'canceled') return;
  toast.success('Purchase complete — unlocking the template for this workspace.');
  const workspaceId = await resolveWorkspaceId();
  if (!workspaceId || !paid.value) return;
  const unlocked = await pollUntilOwned(workspaceId, templateId);
  if (unlocked) toast.success('Template unlocked — you can install it now.');
}

async function buy(): Promise<void> {
  const workspaceId = await resolveWorkspaceId();
  if (!workspaceId) {
    purchaseError.value = 'No workspace is selected. Pick a workspace in the sidebar, then buy the template for it.';
    return;
  }
  purchasing.value = true;
  purchaseError.value = null;
  const base = `${window.location.origin}/library/templates/${templateId}`;
  try {
    const url = await marketplaceService.purchase(workspaceId, templateId, {
      successUrl: `${base}?purchase=success`,
      cancelUrl: `${base}?purchase=canceled`,
    });
    window.location.href = url;
  } catch (err: unknown) {
    purchaseError.value = isForbiddenError(err)
      ? OWNER_ONLY_PURCHASE_MESSAGE
      : `Couldn't start checkout. ${toUserFacingError(err).message}`;
    purchasing.value = false;
  }
}

async function refreshAccess(): Promise<void> {
  await loadAccess();
  if (!locked.value) checkoutReturn.value = null;
}

async function confirmInstall(name: string): Promise<void> {
  const workspaceId = await resolveWorkspaceId();
  if (!workspaceId || !template.value) {
    installModal.value?.fail('No workspace is selected. Pick a workspace, then install the template.');
    return;
  }
  try {
    const workflowId = await templateService.install(workspaceId, template.value.id, name);
    installModalOpen.value = false;
    await router.push({
      name: 'workflow-editor',
      params: { id: workflowId },
      query: { workspaceId },
    });
  } catch (err: unknown) {
    if (isPurchaseRequiredError(err)) {
      installModalOpen.value = false;
      purchaseError.value =
        "This is a paid template and this workspace doesn't own it yet. Buy it to unlock install.";
      await loadTemplateAccess(workspaceId, template.value.id);
      return;
    }
    installModal.value?.fail(`Could not install the template. ${toUserFacingError(err).message}`);
  }
}

onMounted(async () => {
  await load();
  await handleCheckoutReturn();
});
</script>

<template>
  <div class="space-y-6 px-8 py-6">
    <button
      type="button"
      class="flex items-center gap-1 text-sm text-slate-500 hover:text-slate-700"
      @click="router.push({ name: 'library', query: { tab: 'templates' } })"
    >
      <ArrowLeft class="h-4 w-4" />
      Back to templates
    </button>

    <FmAlert
      v-if="checkoutReturn === 'success'"
      type="success"
      dismissible
    >
      <p class="font-medium">
        Purchase complete — it can take a few seconds to unlock; refresh if Install is still locked.
      </p>
      <p
        v-if="polling"
        class="mt-1 flex items-center gap-1.5 text-xs"
      >
        <FmSpinner size="sm" />
        Waiting for Stripe to confirm the payment…
      </p>
      <button
        v-else-if="locked"
        type="button"
        class="mt-1 inline-flex items-center gap-1 text-xs font-medium underline"
        @click="refreshAccess"
      >
        <RefreshCw class="h-3.5 w-3.5" />
        Refresh access
      </button>
    </FmAlert>
    <FmAlert
      v-else-if="checkoutReturn === 'canceled'"
      type="info"
      dismissible
    >
      Checkout canceled — you weren't charged. You can buy the template whenever you're ready.
    </FmAlert>

    <div
      v-if="loading"
      class="flex justify-center py-16"
    >
      <FmSpinner />
    </div>

    <FmErrorState
      v-else-if="error"
      title="Couldn't load template"
      :description="error"
      action-label="Try again"
      @action="load"
    />

    <div
      v-else-if="template"
      class="grid gap-6 lg:grid-cols-3"
    >
      <!-- Left: details -->
      <div class="space-y-5 lg:col-span-2">
        <div class="flex items-start justify-between gap-3">
          <div>
            <h1 class="text-xl font-semibold text-slate-900">
              {{ template.name }}
            </h1>
            <div class="mt-1 flex flex-wrap items-center gap-2 text-sm text-slate-400">
              <TemplatePriceBadge
                :pricing-model="template.pricingModel"
                :price-cents="template.priceCents"
                :currency="template.currency"
              />
              <template v-if="template.publisherName">
                <span>by {{ template.publisherName }}</span>
                <span>·</span>
              </template>
              <span class="capitalize">{{ template.category }}</span>
              <span>·</span>
              <span>{{ template.installCount }} installs</span>
              <span>·</span>
              <span>v{{ template.version }}</span>
            </div>
          </div>
          <span
            :class="[
              'shrink-0 rounded-full px-2 py-0.5 text-xs font-medium',
              template.isOfficial ? 'bg-blue-100 text-blue-700' : 'bg-green-100 text-green-700',
            ]"
          >
            {{ template.isOfficial ? 'Official' : 'Community' }}
          </span>
        </div>

        <p class="text-sm leading-relaxed text-slate-600">
          {{ template.description }}
        </p>

        <div
          v-if="template.tags.length"
          class="flex flex-wrap gap-2"
        >
          <span
            v-for="tag in template.tags"
            :key="tag"
            class="rounded-full bg-slate-100 px-2 py-0.5 text-xs text-slate-600"
          >
            #{{ tag }}
          </span>
        </div>

        <div
          v-if="nodeSummaryLabel"
          class="rounded-lg border border-slate-200 bg-slate-50 px-4 py-3"
        >
          <p class="text-xs font-medium uppercase tracking-wide text-slate-400">
            This template uses
          </p>
          <p class="mt-1 text-sm text-slate-700">
            {{ nodeSummaryLabel }}
          </p>
        </div>

        <!-- YAML preview (the API withholds it for paid templates the workspace doesn't own) -->
        <div v-if="template.yamlContent || paid">
          <p class="mb-2 text-xs font-medium uppercase tracking-wide text-slate-400">
            Workflow definition
          </p>
          <pre
            v-if="template.yamlContent"
            class="max-h-96 overflow-auto rounded-lg border border-slate-200 bg-slate-900 p-4 text-xs leading-relaxed text-slate-100"
          ><code>{{ template.yamlContent }}</code></pre>
          <p
            v-else
            class="flex items-center gap-2 rounded-lg border border-dashed border-slate-300 bg-slate-50 px-4 py-3 text-sm text-slate-600"
            data-testid="yaml-locked-note"
          >
            <Lock class="h-4 w-4 shrink-0 text-slate-400" />
            The workflow definition is available after purchase.
          </p>
        </div>
      </div>

      <!-- Right: preview + install / buy -->
      <div class="space-y-4">
        <div class="flex h-40 items-center justify-center overflow-hidden rounded-xl border border-slate-200 bg-slate-50">
          <img
            v-if="template.previewImageUrl"
            :src="template.previewImageUrl"
            :alt="template.name"
            class="h-full w-full object-cover"
          >
          <span
            v-else
            class="text-5xl"
          >📋</span>
        </div>

        <!-- Paid: licence check in flight -->
        <div
          v-if="paid && accessLoading && !access"
          class="flex items-center justify-center gap-2 rounded-lg border border-slate-200 px-4 py-2.5 text-sm text-slate-500"
        >
          <FmSpinner size="sm" />
          Checking this workspace's licence…
        </div>

        <!-- Paid: licence check failed -->
        <FmAlert
          v-else-if="paid && accessError && !access"
          type="error"
        >
          <p>{{ accessError }}</p>
          <button
            type="button"
            class="mt-2 inline-flex items-center gap-1 text-sm font-medium underline"
            @click="refreshAccess"
          >
            <RefreshCw class="h-3.5 w-3.5" />
            Check again
          </button>
        </FmAlert>

        <!-- Paid and not owned: buy / subscribe -->
        <div
          v-else-if="paid && locked"
          class="space-y-2"
        >
          <FmAlert
            :type="cameToInstall ? 'warning' : 'info'"
            data-testid="paid-locked-notice"
          >
            This is a paid template — buy it for this workspace to unlock install.
          </FmAlert>
          <button
            type="button"
            :disabled="purchasing || polling"
            class="flex w-full items-center justify-center gap-2 rounded-lg bg-indigo-600 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-indigo-700 disabled:opacity-60"
            data-test="buy-template"
            @click="buy"
          >
            <FmSpinner
              v-if="purchasing"
              size="sm"
            />
            <ShoppingCart
              v-else
              class="h-4 w-4"
            />
            {{ purchasing ? 'Opening secure checkout…' : buyLabel }}
          </button>
          <button
            type="button"
            disabled
            class="flex w-full cursor-not-allowed items-center justify-center gap-2 rounded-lg border border-slate-200 px-4 py-2 text-sm font-medium text-slate-400"
          >
            <Lock class="h-4 w-4" />
            Install unlocks after purchase
          </button>
          <p class="text-xs text-slate-500">
            {{ template.pricingModel === 'monthly'
              ? 'Billed monthly to your organisation. Cancel any time from Settings → Purchases.'
              : 'One-time payment. The template stays unlocked for this workspace.' }}
          </p>
          <StripeTestModeHint />
        </div>

        <!-- Free, or owned -->
        <button
          v-else
          type="button"
          class="flex w-full items-center justify-center gap-2 rounded-lg bg-indigo-600 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-indigo-700"
          data-test="install-template"
          @click="installModalOpen = true"
        >
          <Download class="h-4 w-4" />
          Install template
        </button>

        <FmAlert
          v-if="purchaseError"
          type="warning"
        >
          {{ purchaseError }}
        </FmAlert>
        <FmAlert
          v-if="accessNote"
          :type="accessNote.type"
        >
          {{ accessNote.text }}
        </FmAlert>
      </div>
    </div>

    <InstallTemplateModal
      ref="installModal"
      v-model="installModalOpen"
      :template="template"
      @confirm="confirmInstall"
    />
  </div>
</template>
