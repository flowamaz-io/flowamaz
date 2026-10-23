import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { TemplateAccess } from '@/types/marketplace';

const accessMock = vi.fn<(ws: string, id: string) => Promise<TemplateAccess>>();

vi.mock('@/services/marketplace.service', () => ({
  marketplaceService: { access: (ws: string, id: string) => accessMock(ws, id) },
}));

const { useTemplateAccess } = await import('@/composables/useTemplateAccess');

const locked: TemplateAccess = {
  requiresPurchase: true,
  owned: false,
  status: 'none',
  currentPeriodEnd: null,
  cancelAtPeriodEnd: false,
};
const owned: TemplateAccess = { ...locked, owned: true, status: 'active' };

describe('useTemplateAccess', () => {
  beforeEach(() => {
    accessMock.mockReset();
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('loads access and exposes the locked flag', async () => {
    accessMock.mockResolvedValueOnce(locked);
    const a = useTemplateAccess();
    await a.load('ws1', 't1');
    expect(accessMock).toHaveBeenCalledWith('ws1', 't1');
    expect(a.locked.value).toBe(true);
    expect(a.loading.value).toBe(false);
  });

  it('is unlocked for owned or free templates', async () => {
    accessMock.mockResolvedValueOnce(owned);
    const a = useTemplateAccess();
    await a.load('ws1', 't1');
    expect(a.locked.value).toBe(false);

    accessMock.mockResolvedValueOnce({ ...locked, requiresPurchase: false });
    await a.load('ws1', 't1');
    expect(a.locked.value).toBe(false);
  });

  it('surfaces an actionable error when the access check fails', async () => {
    accessMock.mockRejectedValueOnce(new Error('Network down'));
    const a = useTemplateAccess();
    const result = await a.load('ws1', 't1');
    expect(result).toBeNull();
    expect(a.error.value).toContain("Couldn't check whether this workspace owns the template");
    expect(a.error.value).toContain('Network down');
  });

  it('polls until the purchase unlocks the template', async () => {
    accessMock
      .mockResolvedValueOnce({ ...locked, status: 'pending' })
      .mockRejectedValueOnce(new Error('blip'))
      .mockResolvedValueOnce(owned);
    const a = useTemplateAccess();
    const promise = a.pollUntilOwned('ws1', 't1', { attempts: 5, intervalMs: 1000 });
    expect(a.polling.value).toBe(true);
    await vi.runAllTimersAsync();
    await expect(promise).resolves.toBe(true);
    expect(accessMock).toHaveBeenCalledTimes(3);
    expect(a.locked.value).toBe(false);
    expect(a.polling.value).toBe(false);
  });

  it('gives up after the configured attempts', async () => {
    accessMock.mockResolvedValue(locked);
    const a = useTemplateAccess();
    const promise = a.pollUntilOwned('ws1', 't1', { attempts: 3, intervalMs: 500 });
    await vi.runAllTimersAsync();
    await expect(promise).resolves.toBe(false);
    expect(accessMock).toHaveBeenCalledTimes(3);
    expect(a.locked.value).toBe(true);
    expect(a.polling.value).toBe(false);
  });
});
