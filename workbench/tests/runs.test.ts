import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { RunDetail, RunDetailPage, RunsPage, RunTable, WORKBENCH_HOST, canCancelRun, testRunExpiry, type WorkbenchApi } from '../src';
import type { InstanceDetailResponse, InstanceListItem, TimelineResponse } from '../src/types';

const run = (over: Partial<InstanceDetailResponse> = {}): InstanceDetailResponse => ({
  id: 'aaaaaaaa-1111-2222-3333-444444444444', workflowDefinitionId: 'wf-1', workflowVersionId: 'v1',
  status: 'Waiting', triggerType: 'Api', correlationId: null, startedAt: '2026-11-16T00:00:00Z', completedAt: null,
  failedAt: null, errorMessage: null, currentNodeId: 'approve',
  nodeStates: [{ nodeId: 'approve', nodeType: 'HumanGate', status: 'Running', startedAt: null, completedAt: null, retryCount: 0, errorMessage: null }],
  variables: [{ name: 'amount', value: '5', isSensitive: false }],
  events: [{ sequenceNumber: 1, eventType: 'InstanceStarted', nodeId: null, nodeType: null, payload: '{"amount":5}', occurredAt: '2026-11-16T00:00:00Z', inputSnapshot: null, outputSnapshot: null, errorSnapshot: null, durationMs: null }],
  ...over,
});

const timeline: TimelineResponse = {
  instanceId: 'x', totalDurationMs: 10, startedAt: null, completedAt: null,
  nodes: [{ nodeId: 'approve', nodeType: 'HumanGate', label: 'Manager approval', status: 'Running', startedAt: null, completedAt: null, durationMs: 10, offsetMs: 0, retryCount: 0, hasOutput: false }],
};

const listItem = (id: string, over: Partial<InstanceListItem> = {}): InstanceListItem => ({
  id, workflowDefinitionId: 'wf-1', status: 'Completed', triggerType: 'Manual', startedAt: null, completedAt: null,
  createdAt: '2026-11-16T00:00:00Z', isTest: false, testExpiresAt: null, ...over,
});

function fakeApi(over: Partial<WorkbenchApi> = {}): WorkbenchApi {
  const notUsed = () => Promise.reject(new Error('not used in this test'));
  return {
    listWorkflows: vi.fn(async () => ({ items: [{ id: 'wf-1', name: 'Leave request', slug: 'leave', status: 'Published' as const, healthScore: 100, updatedAt: '' }], total: 1, page: 1, pageSize: 100 })),
    getWorkflow: notUsed, createWorkflow: notUsed, saveWorkflow: notUsed, deleteWorkflow: notUsed, publishWorkflow: notUsed,
    archiveWorkflow: notUsed, unarchiveWorkflow: notUsed, listVersions: notUsed, startRun: notUsed,
    listRuns: vi.fn(async () => ({ items: [], total: 0, page: 1, pageSize: 100 })),
    getRun: vi.fn(async () => run()), listRunEvents: notUsed,
    listRunVariables: vi.fn(async () => run().variables),
    getRunTimeline: vi.fn(async () => timeline),
    cancelRun: vi.fn(async () => run({ status: 'Cancelled' })), retryRun: notUsed,
    listGates: notUsed, decideGate: notUsed,
    ...over,
  };
}

const host = (api: WorkbenchApi) => ({ global: { provide: { [WORKBENCH_HOST as symbol]: { api } } } });

describe('run helpers', () => {
  it('knows which runs can be cancelled and when a test run expires', () => {
    expect(canCancelRun('Waiting')).toBe(true);
    expect(canCancelRun('Completed')).toBe(false);
    const now = Date.parse('2026-11-16T00:00:00Z');
    expect(testRunExpiry('2026-11-16T03:20:00Z', now)).toBe('Expires in 3h 20m');
    expect(testRunExpiry('2026-11-15T00:00:00Z', now)).toBe('Expired');
    expect(testRunExpiry(null, now)).toBe('');
  });
});

describe('RunTable', () => {
  it('shows the workflow name and opens a run on click', async () => {
    const wrapper = mount(RunTable, { props: { runs: [listItem('12345678-aaaa', { isTest: true })], workflowName: () => 'Leave request' } });
    expect(wrapper.text()).toContain('Leave request');
    expect(wrapper.text()).toContain('TEST');
    await wrapper.get('tbody tr').trigger('click');
    expect(wrapper.emitted('open')![0]).toEqual(['12345678-aaaa']);
  });
});

describe('RunDetail', () => {
  it('hides the variables tab when data is hidden and adds the host tabs', async () => {
    const wrapper = mount(RunDetail, {
      props: { run: run({ dataHidden: true }), timeline, extraTabs: ['narrative'] },
      slots: { 'tab-narrative': '<p>The story</p>' },
      ...host(fakeApi()),
    });
    const tabs = wrapper.findAll('button').map((b) => b.text());
    expect(tabs).toEqual(['timeline', 'nodes', 'events', 'narrative']);
    expect(wrapper.find('[data-testid="run-data-hidden"]').exists()).toBe(true);
    await wrapper.findAll('button').find((b) => b.text() === 'narrative')!.trigger('click');
    expect(wrapper.text()).toContain('The story');
  });

  it('offers cancel only to someone who may act, while the run can be cancelled', () => {
    expect(mount(RunDetail, { props: { run: run(), timeline }, ...host(fakeApi()) }).text()).not.toContain('Cancel');
    expect(mount(RunDetail, { props: { run: run(), timeline, canAct: true }, ...host(fakeApi()) }).text()).toContain('Cancel');
    expect(mount(RunDetail, { props: { run: run({ status: 'Completed' }), timeline, canAct: true }, ...host(fakeApi()) }).text()).not.toContain('Cancel');
  });
});

describe('RunDetailPage', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('polls while the run is going and stops once it has ended', async () => {
    const statuses = ['Waiting', 'Completed'];
    const api = fakeApi({ getRun: vi.fn(async () => run({ status: (statuses.shift() ?? 'Completed') as InstanceDetailResponse['status'] })) });
    const wrapper = mount(RunDetailPage, { props: { runId: 'r1', pollMs: 1000 }, ...host(api) });
    await flushPromises();
    expect(wrapper.text()).toContain('Waiting');

    await vi.advanceTimersByTimeAsync(1000);
    await flushPromises();
    expect(wrapper.text()).toContain('Completed');

    await vi.advanceTimersByTimeAsync(5000);
    expect(api.getRun).toHaveBeenCalledTimes(2);
  });

  it('cancels and shows the server message when an action fails', async () => {
    const api = fakeApi({ cancelRun: vi.fn(async () => { throw new Error('This run has already ended. Refresh to see its result.'); }) });
    const wrapper = mount(RunDetailPage, { props: { runId: 'r1' }, ...host(api) });
    await flushPromises();
    await wrapper.findAll('button').find((b) => b.text() === 'Cancel')!.trigger('click');
    await flushPromises();
    expect(wrapper.get('[role="alert"]').text()).toContain('already ended');
  });

  it('says the run was not found and offers the way back', async () => {
    const { WorkbenchApiError } = await import('../src');
    const api = fakeApi({ getRun: vi.fn(async () => { throw new WorkbenchApiError("No run 'r1' in this workspace.", 'RUN_NOT_FOUND', 404); }) });
    const wrapper = mount(RunDetailPage, { props: { runId: 'r1' }, ...host(api) });
    await flushPromises();
    expect(wrapper.text()).toContain('Run not found');
    await wrapper.get('button').trigger('click');
    expect(wrapper.emitted('back')).toHaveLength(1);
  });
});

describe('RunsPage', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('lists production runs with workflow names, and teaches when the test tab is empty', async () => {
    const api = fakeApi({ listRuns: vi.fn(async () => ({ items: [listItem('run-1')], total: 1, page: 1, pageSize: 100 })) });
    const wrapper = mount(RunsPage, host(api));
    await flushPromises();
    expect(wrapper.text()).toContain('Leave request');

    await wrapper.findAll('button').find((b) => b.text() === 'test')!.trigger('click');
    await flushPromises();
    expect(wrapper.text()).toContain('No test runs yet');
  });

  it('polls only while a run is still going', async () => {
    const api = fakeApi({ listRuns: vi.fn(async () => ({ items: [listItem('run-1')], total: 1, page: 1, pageSize: 100 })) });
    mount(RunsPage, { props: { pollMs: 1000 }, ...host(api) });
    await flushPromises();
    await vi.advanceTimersByTimeAsync(3000);
    expect(api.listRuns).toHaveBeenCalledTimes(1);
  });
});

describe('RunsPage tabs and filters', () => {
  it('opens on the given tab, reports tab changes, and says when a filter matches nothing', async () => {
    const api = fakeApi({ listRuns: vi.fn(async () => ({ items: [], total: 0, page: 1, pageSize: 100 })) });
    const wrapper = mount(RunsPage, { props: { initialTab: 'test' }, ...host(api) });
    await flushPromises();
    expect(api.listRuns).toHaveBeenCalledWith(expect.objectContaining({ includeTest: true }));
    expect(wrapper.text()).toContain('No test runs yet');

    await wrapper.get('select').setValue('Failed');
    await flushPromises();
    expect(wrapper.text()).toContain('No runs match');
    expect(wrapper.text()).toContain('No test runs are Failed');

    await wrapper.findAll('button').find((b) => b.text() === 'production')!.trigger('click');
    expect(wrapper.emitted('tab-change')![0]).toEqual(['production']);
  });
});

describe('RunDetailPage back link', () => {
  it('says whether the run was a test run, so the host can return to the right tab', async () => {
    const api = fakeApi({ getRun: vi.fn(async () => run({ status: 'Completed', isTest: true })) });
    const wrapper = mount(RunDetailPage, { props: { runId: 'r1' }, ...host(api) });
    await flushPromises();
    await wrapper.findAll('button').find((b) => b.text().includes('Runs'))!.trigger('click');
    expect(wrapper.emitted('back')![0]).toEqual([true]);
  });
});

