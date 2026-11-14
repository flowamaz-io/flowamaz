import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import TwinPanel from '@/components/workflow/TwinPanel.vue';
import { twinBadge, type TwinLatest, type TwinRun } from '@/services/twin.service';

const latest = vi.fn();
const run = vi.fn();
vi.mock('@/services/twin.service', async (orig) => ({
  ...(await orig<typeof import('@/services/twin.service')>()),
  twinService: {
    latest: (ws: string, id: string) => latest(ws, id),
    run: (ws: string, id: string) => run(ws, id),
  },
}));

const result = (over: Partial<TwinRun> = {}): TwinRun => ({
  id: 'r1',
  workflowDefinitionId: 'wf1',
  versionLabel: 'production',
  trigger: 'scheduled',
  status: 'breaks',
  casesRun: 1240,
  routersTested: 3,
  breakCount: 1,
  warningCount: 0,
  durationMs: 42,
  completedAt: new Date().toISOString(),
  errorMessage: null,
  findings: [
    {
      kind: 'no-route',
      severity: 'break',
      nodeId: 'size',
      nodeLabel: 'Amount check',
      title: 'Breaks when amount = 5000',
      explanation: 'No branch of "Amount check" matches when amount = 5000.',
      fix: 'Nothing handles exactly 5000: "Small" uses < and "Large" uses >.',
      example: { amount: '5000' },
      alsoFailsWith: [],
      cases: 3,
      fromRealPayload: false,
    },
    {
      kind: 'never-taken',
      severity: 'info',
      nodeId: 'size',
      nodeLabel: 'Amount check',
      title: 'Branch "Huge" was never taken',
      explanation: 'None of 1,240 simulated runs took it.',
      fix: 'Check the condition.',
      example: {},
      alsoFailsWith: [],
      cases: 0,
      fromRealPayload: false,
    },
  ],
  ...over,
});

function mountPanel(canRun = true) {
  return mount(TwinPanel, { props: { workspaceId: 'ws1', workflowId: 'wf1', canRun } });
}

describe('Workflow Twin panel', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    latest.mockReset();
    run.mockReset();
  });

  it('shows loading, then the findings with the example and fix; notes are hidden until asked for', async () => {
    let resolve: (v: TwinLatest) => void = () => {};
    latest.mockReturnValue(new Promise((r) => (resolve = r)));
    const w = mountPanel();
    expect(w.find('[data-testid="twin-loading"]').exists()).toBe(true);
    resolve({ run: result(), hasProductionVersion: true });
    await flushPromises();
    const findings = w.findAll('[data-testid="twin-finding"]');
    expect(findings).toHaveLength(1);
    expect(findings[0]!.text()).toContain('Breaks when amount = 5000');
    expect(findings[0]!.text()).toContain('Fix:');
    expect(findings[0]!.text()).toContain('amount:');
    expect(w.get('[data-testid="twin-status"]').text()).toBe('1 break');
    expect(w.get('[data-testid="twin-meta"]').text()).toContain('1,240 inputs');
    await w.get('[data-testid="twin-toggle-info"]').trigger('click');
    expect(w.findAll('[data-testid="twin-finding"]')).toHaveLength(2);
  });

  it('teaches what to do before the first run, and runs on request', async () => {
    latest.mockResolvedValue({ run: null, hasProductionVersion: false });
    run.mockResolvedValue(result({ status: 'passed', breakCount: 0, findings: [] }));
    const w = mountPanel();
    await flushPromises();
    expect(w.get('[data-testid="twin-empty"]').text()).toContain('Run the Twin');
    await w.get('[data-testid="twin-run"]').trigger('click');
    await flushPromises();
    expect(run).toHaveBeenCalledWith('ws1', 'wf1');
    expect(w.get('[data-testid="twin-clean"]').text()).toContain('No input broke this workflow');
  });

  it('hides the run button from people who cannot design, and says who can run it', async () => {
    latest.mockResolvedValue({ run: null, hasProductionVersion: false });
    const w = mountPanel(false);
    await flushPromises();
    expect(w.find('[data-testid="twin-run"]').exists()).toBe(false);
    expect(w.get('[data-testid="twin-empty"]').text()).toContain('A Designer or Admin');
  });

  it('explains a load failure and a failed simulation', async () => {
    latest.mockRejectedValueOnce(new Error('The API is unreachable. Check your connection and try again.'));
    const w = mountPanel();
    await flushPromises();
    expect(w.text()).toContain("Couldn't load the Twin result");

    latest.mockResolvedValue({ run: result({ status: 'failed', findings: [], errorMessage: "The Twin couldn't read this workflow: …" }), hasProductionVersion: true });
    const w2 = mountPanel();
    await flushPromises();
    expect(w2.get('[data-testid="twin-failed"]').text()).toContain("couldn't read this workflow");
  });

  it('labels badges by result', () => {
    expect(twinBadge('breaks', 2, 0)).toEqual({ label: '2 breaks', variant: 'danger' });
    expect(twinBadge('warnings', 0, 1)).toEqual({ label: '1 warning', variant: 'amber' });
    expect(twinBadge('passed', 0, 0).label).toBe('No breaks');
    expect(twinBadge(undefined, 0, 0).label).toBe('Not simulated');
  });
});
