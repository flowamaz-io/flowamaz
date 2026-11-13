import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import EmpathyPanel from '@/components/editor/EmpathyPanel.vue';
import type { EmpathyAnalysis } from '@/services/empathy.service';

// Empathy View with real-run evidence (§8.2), and the note shown before a workflow has runs.

let analysis: EmpathyAnalysis;
vi.mock('@/services/empathy.service', () => ({ empathyService: { getEmpathyAnalysis: async () => analysis } }));

const base: EmpathyAnalysis = {
  workflowDefinitionId: 'w', emailsSentToRequester: 1, waitPeriodCount: 1, statusUpdateCount: 1,
  avgDaysToOutcome: 3, visibilityGapHours: 80, score: 60, issues: [], runs: null,
};

const mountIt = async () => {
  const w = mount(EmpathyPanel, { props: { workspaceId: 'ws', workflowId: 'w', visible: true }, global: { stubs: { FmTooltip: true } } });
  await flushPromises();
  return w;
};

describe('EmpathyPanel real runs', () => {
  beforeEach(() => {
    analysis = { ...base };
  });

  it('shows what real runs showed', async () => {
    analysis = {
      ...base,
      runs: {
        runsAnalysed: 12, avgDaysToOutcome: 3, avgMessagesPerRun: 7, medianLongestSilenceHours: 80,
        percentTimeWaitingOnApprovals: 85, percentRunsOutcomeNotified: 20, slowestGateNodeId: 'it', slowestGateAvgHours: 60,
      },
    };
    const w = await mountIt();
    const runs = w.find('[data-test="empathy-runs"]');
    expect(runs.text()).toContain('From 12 real runs (last 30 days)');
    expect(runs.text()).toContain('3.3 days');
    expect(runs.text()).toContain('85%');
    expect(runs.text()).toContain('20%');
  });

  it('explains that run data appears once the workflow has finished runs', async () => {
    const w = await mountIt();
    expect(w.find('[data-test="empathy-no-runs"]').text()).toContain("test runs don't count");
  });
});
