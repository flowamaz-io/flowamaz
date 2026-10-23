import { computed, ref } from 'vue';
import { marketplaceService } from '@/services/marketplace.service';
import { toUserFacingError } from '@/utils/error.util';
import type { TemplateAccess } from '@/types/marketplace';

export interface PollOptions {
  attempts?: number;
  intervalMs?: number;
}

const wait = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * Tracks whether the current workspace may install a (possibly paid) template, and polls
 * after a Stripe Checkout return until the webhook-driven purchase unlocks it.
 */
export function useTemplateAccess() {
  const access = ref<TemplateAccess | null>(null);
  const loading = ref(false);
  const polling = ref(false);
  const error = ref<string | null>(null);

  /** True when the template must be bought before it can be installed. */
  const locked = computed<boolean>(() => !!access.value && access.value.requiresPurchase && !access.value.owned);

  async function load(workspaceId: string, templateId: string): Promise<TemplateAccess | null> {
    loading.value = true;
    error.value = null;
    try {
      access.value = await marketplaceService.access(workspaceId, templateId);
      return access.value;
    } catch (e) {
      error.value = `Couldn't check whether this workspace owns the template. ${toUserFacingError(e).message}`;
      return null;
    } finally {
      loading.value = false;
    }
  }

  /** Re-checks access up to `attempts` times; resolves true as soon as the template is owned. */
  async function pollUntilOwned(
    workspaceId: string,
    templateId: string,
    { attempts = 5, intervalMs = 2000 }: PollOptions = {},
  ): Promise<boolean> {
    polling.value = true;
    try {
      for (let i = 0; i < attempts; i++) {
        try {
          const next = await marketplaceService.access(workspaceId, templateId);
          access.value = next;
          error.value = null;
          if (next.owned || !next.requiresPurchase) return true;
        } catch {
          // Transient failure while the webhook settles — keep trying.
        }
        if (i < attempts - 1) await wait(intervalMs);
      }
      return false;
    } finally {
      polling.value = false;
    }
  }

  return { access, loading, polling, error, locked, load, pollUntilOwned };
}
