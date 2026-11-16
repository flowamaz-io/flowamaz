import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { GatesPage, WORKBENCH_HOST, WorkbenchApiError, humanizeId, type WorkbenchApi } from '../src';
import type { GateResponse } from '../src/types';

const gate = (over: Partial<GateResponse> = {}): GateResponse => ({
  id: 'g1', instanceId: 'aaaaaaaa-1111', nodeId: 'manager-approval', decision: 'Pending', assignedToEmail: 'boss@example.com',
  deliveryChannel: 'Email', deliveryStatus: 'Sent', expiresAt: null, createdAt: '2026-11-16T00:00:00Z', decidedAt: null,
  decisionNote: null, gateLabel: null, form: null, formValues: null, ...over,
});

function fakeApi(gates: GateResponse[], over: Partial<WorkbenchApi> = {}): WorkbenchApi {
  const notUsed = () => Promise.reject(new Error('not used in this test'));
  return {
    listWorkflows: notUsed, getWorkflow: notUsed, createWorkflow: notUsed, saveWorkflow: notUsed, deleteWorkflow: notUsed,
    publishWorkflow: notUsed, archiveWorkflow: notUsed, unarchiveWorkflow: notUsed, listVersions: notUsed, startRun: notUsed,
    listRuns: notUsed, getRun: notUsed, listRunEvents: notUsed, listRunVariables: notUsed, getRunTimeline: notUsed,
    cancelRun: notUsed, retryRun: notUsed,
    listGates: vi.fn(async () => gates),
    decideGate: vi.fn(async (_run: string, _node: string, decision: 'approved' | 'rejected', note?: string) =>
      gate({ decision: decision === 'approved' ? 'Approved' : 'Rejected', decisionNote: note ?? null, decidedAt: '2026-11-16T01:00:00Z' })),
    ...over,
  };
}

const host = (api: WorkbenchApi) => ({ global: { provide: { [WORKBENCH_HOST as symbol]: { api } } }, attachTo: document.body });

describe('GatesPage', () => {
  beforeEach(() => localStorage.clear());

  it('lists the waiting gate by its label, and keeps it after approving so the decision is seen', async () => {
    const api = fakeApi([gate()]);
    const wrapper = mount(GatesPage, host(api));
    await flushPromises();
    expect(wrapper.text()).toContain('Manager approval');
    expect(api.listGates).toHaveBeenCalledWith({ includeTest: false, includeDecided: true });

    await wrapper.get('input[placeholder="Optional note…"]').setValue('Looks fine');
    await wrapper.get('[data-testid="gate-approve-manager-approval"]').trigger('click');
    await flushPromises();

    expect(api.decideGate).toHaveBeenCalledWith('aaaaaaaa-1111', 'manager-approval', 'approved', 'Looks fine', undefined);
    expect(wrapper.emitted('decided')).toHaveLength(1);
    expect(wrapper.text()).toContain('Approved');
    wrapper.unmount();
  });

  it('opens the form first when the gate asks for details', async () => {
    const api = fakeApi([gate({ form: [{ key: 'amount', label: 'Amount', type: 'number', required: true }] })]);
    const wrapper = mount(GatesPage, host(api));
    await flushPromises();
    expect(wrapper.get('[data-testid="gate-has-form"]').text()).toContain('Asks for 1 detail');

    await wrapper.get('[data-testid="gate-approve-manager-approval"]').trigger('click');
    await flushPromises();

    expect(api.decideGate).not.toHaveBeenCalled();
    expect(document.querySelector('[data-testid="gate-approve-submit"]')).not.toBeNull();
    wrapper.unmount();
  });

  it('shows the server message when a decision fails', async () => {
    const api = fakeApi([gate()], {
      decideGate: vi.fn(async () => { throw new WorkbenchApiError('This run has already ended, so the approval no longer applies.', 'GATE_RUN_ENDED', 409); }),
    });
    const wrapper = mount(GatesPage, host(api));
    await flushPromises();
    await wrapper.findAll('button').find((b) => b.text().includes('Reject'))!.trigger('click');
    await flushPromises();
    expect(wrapper.text()).toContain('already ended');
    wrapper.unmount();
  });

  it('remembers "Include test runs" and teaches when nothing is waiting', async () => {
    const api = fakeApi([]);
    const wrapper = mount(GatesPage, { ...host(api), props: { showTestStorageKey: 'k' } });
    await flushPromises();
    expect(wrapper.text()).toContain('No pending approvals');
    expect(wrapper.text()).toContain('tick Include test runs');

    await wrapper.get('[data-testid="gates-show-test"]').trigger('change');
    await flushPromises();
    expect(localStorage.getItem('k')).toBe('true');
    expect(api.listGates).toHaveBeenLastCalledWith({ includeTest: true, includeDecided: true });
    wrapper.unmount();
  });
});

describe('humanizeId', () => {
  it('turns node ids into words', () => {
    expect(humanizeId('manager-approval')).toBe('Manager approval');
    expect(humanizeId('riskReview')).toBe('Risk review');
  });
});
