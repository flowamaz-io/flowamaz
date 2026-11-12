import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import InheritancePanel from '@/components/editor/InheritancePanel.vue';
import type { InheritanceReport } from '@/services/inheritance.service';

// Patterns from similar, better-performing workflows (§8.8): list, apply, read-only, empty and error states.

let report: InheritanceReport;
let fail = false;
const getReport = vi.fn(async () => {
  if (fail) throw new Error('down');
  return report;
});
const apply = vi.fn(async () => 'yaml');
vi.mock('@/services/inheritance.service', () => ({
  inheritanceService: { getReport: () => getReport(), apply: (...a: unknown[]) => apply(...(a as [])) },
}));
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));
vi.mock('@/utils/error.util', () => ({ toUserFacingError: () => ({ message: 'Unavailable. Try again.' }) }));

const timeout = {
  kind: 'gate-timeout' as const, fromWorkflowId: 'east', fromWorkflowName: 'PO approval (East)', similarity: 90,
  evidence: '95% of its runs succeed, against 70% here.', title: 'Add a timeout to approval steps',
  description: "'PO approval (East)' gives its approvers 24 hours.", canApply: true,
};
const notify = { ...timeout, kind: 'outcome-notification' as const, title: 'Tell people the outcome', canApply: false };

const mountIt = async (canEdit = true) => {
  const w = mount(InheritancePanel, { props: { workspaceId: 'ws', workflowId: 'west', canEdit } });
  await flushPromises();
  return w;
};

describe('InheritancePanel', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    fail = false;
    report = { workflowId: 'west', performance: { runs: 20, successPercent: 70, slaCompliancePercent: 90, avgHours: 5 }, suggestions: [timeout, notify], message: null };
    apply.mockClear();
  });

  it('shows each pattern with where it comes from and why', async () => {
    const w = await mountIt();
    expect(w.findAll('[data-test="inheritance-suggestion"]')).toHaveLength(2);
    expect(w.text()).toContain('From PO approval (East) (90% similar). 95% of its runs succeed');
    expect(w.text()).toContain('Make this change on the canvas');
  });

  it('adds an applicable pattern and tells the editor to reload', async () => {
    const w = await mountIt();
    await w.findAll('button').find((b) => b.text() === 'Add to this workflow')!.trigger('click');
    await flushPromises();
    expect(apply).toHaveBeenCalledWith('ws', 'west', 'gate-timeout', 'east');
    expect(w.emitted('applied')).toHaveLength(1);
  });

  it('offers nothing to apply to people who cannot edit', async () => {
    const w = await mountIt(false);
    expect(w.text()).not.toContain('Add to this workflow');
    expect(w.text()).toContain('A Designer or Admin can add this here.');
  });

  it('explains an empty list', async () => {
    report = { ...report, suggestions: [], message: 'This workflow has 2 finished runs in the last 30 days.' };
    const w = await mountIt();
    expect(w.find('[data-test="inheritance-empty"]').text()).toContain('2 finished runs');
  });

  it('offers a retry when loading fails', async () => {
    fail = true;
    const w = await mountIt();
    expect(w.text()).toContain("Couldn't load suggestions");
  });
});
