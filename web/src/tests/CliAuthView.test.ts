import { describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import CliAuthView from '@/views/cli/CliAuthView.vue';
import { normaliseUserCode } from '@/services/cliAuth.service';

const approve = vi.fn();
vi.mock('@/services/cliAuth.service', async (orig) => ({
  ...(await orig<typeof import('@/services/cliAuth.service')>()),
  cliAuthService: { approve: (c: string) => approve(c) },
}));
let query: Record<string, string> = {};
vi.mock('vue-router', () => ({ useRoute: () => ({ query }) }));

describe('CLI sign-in page (/cli-auth)', () => {
  it('normalises codes the way the CLI prints them', () => {
    expect(normaliseUserCode(' abcd2345 ')).toBe('ABCD-2345');
    expect(normaliseUserCode('abcd-2345')).toBe('ABCD-2345');
  });

  it('approves the code from the link and confirms', async () => {
    setActivePinia(createPinia());
    query = { code: 'abcd-2345' };
    approve.mockResolvedValue(undefined);
    const w = mount(CliAuthView);
    expect((w.get('[data-testid="cli-auth-code"]').element as HTMLInputElement).value).toBe('ABCD-2345');
    await w.get('[data-testid="cli-auth-approve"]').trigger('click');
    await flushPromises();
    expect(approve).toHaveBeenCalledWith('ABCD-2345');
    expect(w.text()).toContain('CLI signed in');
  });

  it('explains a bad or expired code and lets you retry', async () => {
    setActivePinia(createPinia());
    query = {};
    const w = mount(CliAuthView);
    await w.get('[data-testid="cli-auth-approve"]').trigger('click');
    expect(w.text()).toContain('Enter the 8-character code');
    approve.mockRejectedValue(new Error('That code is invalid or has expired. Run `fmz auth login` again to get a fresh code.'));
    await w.get('[data-testid="cli-auth-code"]').setValue('ZZZZ-9999');
    await w.get('[data-testid="cli-auth-approve"]').trigger('click');
    await flushPromises();
    expect(w.text()).toContain('invalid or has expired');
    expect(w.text()).not.toContain('CLI signed in');
  });
});
