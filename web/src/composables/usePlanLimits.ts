import { computed, ref } from 'vue';
import { platformService } from '@/services/platform.service';
import { useWorkspace } from '@/composables/useWorkspace';
import type { UsageResponse } from '@/types';

/**
 * Loads the current workspace's plan/edition usage (prompt 05-07). Used by the edition banner and
 * the usage bars on the workflow/instance lists. Degrades silently — usage is non-essential UI.
 */
export function usePlanLimits() {
  const ws = useWorkspace();
  const usage = ref<UsageResponse | null>(null);
  const loading = ref(false);

  async function load(): Promise<void> {
    const wsId = ws.currentWorkspaceId.value;
    if (!wsId) return;
    loading.value = true;
    try {
      usage.value = await platformService.usage(wsId);
    } catch {
      usage.value = null;
    } finally {
      loading.value = false;
    }
  }

  const isCommunity = computed(() => usage.value?.isCommunity ?? false);
  const isAtLimit = computed(() => usage.value?.isAtLimit ?? false);
  const workflowsUsed = computed(() => usage.value?.workflowsUsed ?? 0);
  const workflowsLimit = computed(() => usage.value?.workflowsLimit ?? 0);
  const runsUsed = computed(() => usage.value?.runsUsed ?? 0);
  const runsLimit = computed(() => usage.value?.runsLimit ?? 0);

  return { usage, loading, load, isCommunity, isAtLimit, workflowsUsed, workflowsLimit, runsUsed, runsLimit };
}
