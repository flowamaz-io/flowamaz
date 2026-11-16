import { describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import {
  WORKBENCH_HOST, WorkbenchApiError, WorkflowDetailPage, WorkflowsPage, slugify, starterWorkflowYaml, workflowStatusVariant,
  type WorkbenchApi,
} from '../src';
import type { WorkflowDefinitionListItem, WorkflowDefinitionResponse } from '../src/types';

const item = (id: string, status: WorkflowDefinitionListItem['status'], name = id): WorkflowDefinitionListItem => ({
  id, name, slug: id, status, healthScore: 100, updatedAt: '2026-11-16T00:00:00Z',
});
const definition = (over: Partial<WorkflowDefinitionResponse> = {}): WorkflowDefinitionResponse => ({
  id: 'wf-1', workspaceId: 'ws', name: 'Leave request', slug: 'leave', description: null, yamlContent: '', nlDescription: null,
  createdByMethod: 'Canvas', status: 'Published', currentVersion: 'v', healthScore: 100, triggerType: 'Manual',
  createdAt: '', updatedAt: '2026-11-16T00:00:00Z', ...over,
});

function fakeApi(over: Partial<WorkbenchApi> = {}): WorkbenchApi {
  const notUsed = () => Promise.reject(new Error('not used in this test'));
  return {
    listWorkflows: vi.fn(async () => ({ items: [item('leave', 'Published', 'Leave request'), item('draft', 'Draft', 'Expenses'), item('old', 'Archived', 'Old one')], total: 3, page: 1, pageSize: 100 })),
    getWorkflow: vi.fn(async () => definition()),
    createWorkflow: vi.fn(async (r) => definition({ id: 'new', name: r.name, slug: r.slug, status: 'Draft' })),
    saveWorkflow: notUsed, publishWorkflow: notUsed,
    deleteWorkflow: vi.fn(async () => undefined),
    archiveWorkflow: vi.fn(async () => definition({ status: 'Archived' })),
    unarchiveWorkflow: vi.fn(async () => definition({ status: 'Published' })),
    listVersions: vi.fn(async () => [{ id: 'v1', workflowDefinitionId: 'wf-1', commitSha: 'abcdef123456', tagName: 'wf-1/v1', branchName: 'main', message: 'Published', isProduction: true, createdAt: '2026-11-16T00:00:00Z' }]),
    startRun: vi.fn(async () => ({ instanceId: 'run-9', status: 'Pending' as const, triggeredAt: '' })),
    listRuns: vi.fn(async () => ({ items: [], total: 0, page: 1, pageSize: 50 })),
    getRun: notUsed, listRunEvents: notUsed, listRunVariables: notUsed, getRunTimeline: notUsed, cancelRun: notUsed, retryRun: notUsed,
    listGates: notUsed, decideGate: notUsed,
    ...over,
  };
}

const host = (api: WorkbenchApi) => ({ global: { provide: { [WORKBENCH_HOST as symbol]: { api } } }, attachTo: document.body });
const byText = (wrapper: ReturnType<typeof mount>, text: string) => wrapper.findAll('button').find((b) => b.text().includes(text))!;

describe('workflow helpers', () => {
  it('starts from a workflow with a quoted name, and makes short ids', () => {
    const yaml = starterWorkflowYaml('Leave "special"', 'leave');
    expect(yaml).toContain('name: "Leave \\"special\\""');
    expect(yaml).toContain('type: human-gate');
    expect(slugify('Leave Request #2')).toBe('leave-request-2');
    expect(workflowStatusVariant('Published')).toBe('primary');
  });
});

describe('WorkflowsPage', () => {
  it('hides archived workflows until asked, and offers the right actions per status', async () => {
    const wrapper = mount(WorkflowsPage, host(fakeApi()));
    await flushPromises();
    expect(wrapper.text()).toContain('Leave request');
    expect(wrapper.text()).toContain('Expenses');
    expect(wrapper.text()).not.toContain('Old one');
    expect(wrapper.find('[aria-label="Archive Leave request"]').exists()).toBe(true);
    expect(wrapper.find('[aria-label="Delete Expenses"]').exists()).toBe(true);
    expect(wrapper.find('[aria-label="Delete Leave request"]').exists()).toBe(false);

    await wrapper.get('select').setValue('archived');
    expect(wrapper.text()).toContain('Old one');
    expect(wrapper.find('[aria-label="Unarchive Old one"]').exists()).toBe(true);
    wrapper.unmount();
  });

  it('creates a workflow from a name and opens it for editing', async () => {
    const api = fakeApi();
    const wrapper = mount(WorkflowsPage, host(api));
    await flushPromises();
    await wrapper.get('[data-testid="new-workflow"]').trigger('click');
    const name = document.querySelector('#new-workflow-name') as HTMLInputElement;
    name.value = 'Leave Request';
    name.dispatchEvent(new Event('input'));
    await flushPromises();
    expect((document.querySelector('#new-workflow-slug') as HTMLInputElement).value).toBe('leave-request');
    (document.querySelector('[data-testid="new-workflow-submit"]') as HTMLButtonElement).click();
    await flushPromises();

    expect(api.createWorkflow).toHaveBeenCalledWith(expect.objectContaining({ name: 'Leave Request', slug: 'leave-request', yamlContent: expect.stringContaining('id: leave-request') }));
    expect(wrapper.emitted('edit')![0]).toEqual(['new']);
    wrapper.unmount();
  });

  it('deletes a draft after confirming, and shows the reason when archiving is refused', async () => {
    const api = fakeApi({ archiveWorkflow: vi.fn(async () => { throw new WorkbenchApiError('Only a published workflow can be archived.', 'WORKFLOW_NOT_PUBLISHED', 422); }) });
    const wrapper = mount(WorkflowsPage, host(api));
    await flushPromises();
    await wrapper.get('[aria-label="Delete Expenses"]').trigger('click');
    (document.querySelector('[data-testid="confirm-delete"]') as HTMLButtonElement).click();
    await flushPromises();
    expect(api.deleteWorkflow).toHaveBeenCalledWith('draft');

    await wrapper.get('[aria-label="Archive Leave request"]').trigger('click');
    await flushPromises();
    expect(wrapper.get('[role="alert"]').text()).toContain('Only a published workflow');
    wrapper.unmount();
  });

  it('starts a run with a JSON payload, refusing one that is not JSON', async () => {
    const api = fakeApi();
    const wrapper = mount(WorkflowsPage, host(api));
    await flushPromises();
    await wrapper.get('[aria-label="Start a run of Leave request"]').trigger('click');
    const payload = document.querySelector('#start-run-payload') as HTMLTextAreaElement;
    payload.value = '{oops';
    payload.dispatchEvent(new Event('input'));
    (document.querySelector('[data-testid="start-run-submit"]') as HTMLButtonElement).click();
    await flushPromises();
    expect(api.startRun).not.toHaveBeenCalled();
    expect(document.body.textContent).toContain('must be valid JSON');

    payload.value = '{"days":2}';
    payload.dispatchEvent(new Event('input'));
    (document.querySelector('[data-testid="start-run-submit"]') as HTMLButtonElement).click();
    await flushPromises();
    expect(api.startRun).toHaveBeenCalledWith('leave', { payload: '{"days":2}', isTest: false, idempotencyKey: null });
    expect(wrapper.emitted('run-started')![0]).toEqual(['run-9']);
    wrapper.unmount();
  });

  it('teaches when there are no workflows', async () => {
    const wrapper = mount(WorkflowsPage, host(fakeApi({ listWorkflows: vi.fn(async () => ({ items: [], total: 0, page: 1, pageSize: 100 })) })));
    await flushPromises();
    expect(wrapper.text()).toContain('No workflows yet');
    wrapper.unmount();
  });
});

describe('WorkflowDetailPage', () => {
  it('shows versions, archives, and lists runs', async () => {
    const api = fakeApi();
    const wrapper = mount(WorkflowDetailPage, { ...host(api), props: { workflowId: 'wf-1' } });
    await flushPromises();
    expect(wrapper.text()).toContain('wf-1/v1');
    expect(wrapper.text()).toContain('In use');

    await byText(wrapper, 'Archive').trigger('click');
    await flushPromises();
    expect(api.archiveWorkflow).toHaveBeenCalledWith('wf-1');
    expect(wrapper.text()).toContain('Unarchive');

    await byText(wrapper, 'runs').trigger('click');
    expect(wrapper.text()).toContain('No runs yet');
    expect(api.listRuns).toHaveBeenCalledWith({ workflowId: 'wf-1', includeTest: true, pageSize: 50 });
    wrapper.unmount();
  });

  it('offers a test run for a draft and says when nothing is published', async () => {
    const api = fakeApi({ getWorkflow: vi.fn(async () => definition({ status: 'Draft' })), listVersions: vi.fn(async () => []) });
    const wrapper = mount(WorkflowDetailPage, { ...host(api), props: { workflowId: 'wf-1' } });
    await flushPromises();
    expect(wrapper.get('[data-testid="workflow-start-run"]').text()).toBe('Test run');
    expect(wrapper.text()).toContain('Not published yet');
    wrapper.unmount();
  });
});
