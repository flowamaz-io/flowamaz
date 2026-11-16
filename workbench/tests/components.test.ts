import { describe, expect, it, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import { FmBadge, FmErrorState, WORKBENCH_HOST, useWorkbenchApi } from '../src';
import { defineComponent, h } from 'vue';

describe('FmErrorState', () => {
  it('shows a help link only when the host can open help, and opens the host article', async () => {
    const openHelp = vi.fn();
    const withHelp = mount(FmErrorState, {
      props: { title: 'Could not load runs', description: 'The server is down. Try again.', statusCode: 503 },
      global: { provide: { [WORKBENCH_HOST as symbol]: { openHelp, articleForError: (s?: number) => (s === 503 ? 'ops/down' : null) } } },
    });
    await withHelp.get('button').trigger('click');
    expect(openHelp).toHaveBeenCalledWith('ops/down');

    const without = mount(FmErrorState, { props: { title: 'T', description: 'D', helpArticle: 'x' } });
    expect(without.text()).not.toContain('help article');
  });
});

describe('FmBadge', () => {
  it('uses a variant, or the given tone instead', () => {
    expect(mount(FmBadge, { props: { variant: 'danger' }, slots: { default: 'Failed' } }).classes()).toContain('bg-danger-100');
    const toned = mount(FmBadge, { props: { tone: 'bg-teal-100 text-teal-700' }, slots: { default: 'Admin' } });
    expect(toned.classes()).toContain('bg-teal-100');
    expect(toned.text()).toBe('Admin');
  });
});

describe('useWorkbenchApi', () => {
  it('explains how to install an API when the host has none', () => {
    const Probe = defineComponent({ setup() { useWorkbenchApi(); return () => h('div'); } });
    expect(() => mount(Probe)).toThrow(/installWorkbench/);
  });
});
