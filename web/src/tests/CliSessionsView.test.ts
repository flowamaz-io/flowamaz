import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import CliSessionsView from '@/views/settings/CliSessionsView.vue';
import type { CliSession } from '@/services/cliAuth.service';

const listSessions = vi.fn();
const revokeSession = vi.fn();
vi.mock('@/services/cliAuth.service', async (orig) => ({
  ...(await orig<typeof import('@/services/cliAuth.service')>()),
  cliAuthService: {
    listSessions: () => listSessions(),
    revokeSession: (id: string) => revokeSession(id),
  },
}));

const session = (over: Partial<CliSession> = {}): CliSession => ({
  id: 's1',
  name: 'fmz on jagan-mbp',
  createdAt: '2026-11-10T08:00:00Z',
  lastUsedAt: '2026-11-14T08:00:00Z',
  lastUsedIp: '10.0.0.5',
  expiresAt: '2026-12-14T08:00:00Z',
  isCurrent: false,
  ...over,
});

function mountView() {
  return mount(CliSessionsView, { global: { stubs: { teleport: true } } });
}

describe('Settings → CLI sessions', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    listSessions.mockReset();
    revokeSession.mockReset();
  });

  it('shows a loading state, then the signed-in terminals', async () => {
    let resolve: (v: CliSession[]) => void = () => {};
    listSessions.mockReturnValue(new Promise((r) => (resolve = r)));
    const w = mountView();
    expect(w.find('[data-testid="cli-sessions-loading"]').exists()).toBe(true);
    resolve([session(), session({ id: 's2', name: 'build-box', isCurrent: true })]);
    await flushPromises();
    expect(w.findAll('[data-testid="cli-session-row"]')).toHaveLength(2);
    expect(w.text()).toContain('fmz on jagan-mbp');
    expect(w.text()).toContain('This CLI');
  });

  it('teaches how to sign a terminal in when there are none', async () => {
    listSessions.mockResolvedValue([]);
    const w = mountView();
    await flushPromises();
    expect(w.find('[data-testid="cli-sessions-empty"]').text()).toContain('fmz auth login');
  });

  it('explains a load failure and offers a retry', async () => {
    listSessions.mockRejectedValueOnce(new Error('The API is unreachable. Check your connection and try again.'));
    const w = mountView();
    await flushPromises();
    expect(w.text()).toContain("Couldn't load your CLI sessions");
    listSessions.mockResolvedValue([session()]);
    const retry = w.findAll('button').find((b) => b.text().includes('Try again'));
    expect(retry).toBeDefined();
    await retry!.trigger('click');
    await flushPromises();
    expect(w.findAll('[data-testid="cli-session-row"]')).toHaveLength(1);
  });

  it('signs a terminal out after confirmation and removes it from the list', async () => {
    listSessions.mockResolvedValue([session()]);
    revokeSession.mockResolvedValue(undefined);
    const w = mountView();
    await flushPromises();
    await w.get('[data-testid="cli-session-revoke-s1"]').trigger('click');
    await flushPromises();
    await w.get('[data-testid="cli-session-revoke-confirm"]').trigger('click');
    await flushPromises();
    expect(revokeSession).toHaveBeenCalledWith('s1');
    expect(w.findAll('[data-testid="cli-session-row"]')).toHaveLength(0);
    expect(w.find('[data-testid="cli-sessions-empty"]').exists()).toBe(true);
  });

  it('reloads the list when signing out fails (e.g. it already ended)', async () => {
    listSessions.mockResolvedValue([session()]);
    revokeSession.mockRejectedValue(new Error('That CLI session isn’t active any more.'));
    const w = mountView();
    await flushPromises();
    await w.get('[data-testid="cli-session-revoke-s1"]').trigger('click');
    await flushPromises();
    await w.get('[data-testid="cli-session-revoke-confirm"]').trigger('click');
    await flushPromises();
    expect(listSessions).toHaveBeenCalledTimes(2);
  });
});
