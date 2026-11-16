import { describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { WORKBENCH_HOST, WorkbenchApiError, WorkflowEditorPage, type WorkbenchApi } from '../src';
import type { WorkflowDefinitionResponse } from '../src/types';

const YAML = `apiVersion: flowamaz/v1
kind: Workflow
metadata: { id: leave, name: Leave request }
spec:
  nodes:
    - { id: start, type: trigger }
    - { id: done, type: end }
  edges:
    - { id: e1, from: start, to: done }
`;

const definition = (over: Partial<WorkflowDefinitionResponse> = {}): WorkflowDefinitionResponse => ({
  id: 'wf-1', workspaceId: 'ws', name: 'Leave request', slug: 'leave', description: null, yamlContent: YAML,
  nlDescription: null, createdByMethod: 'Canvas', status: 'Draft', currentVersion: 'draft', healthScore: 100,
  triggerType: 'Manual', createdAt: '', updatedAt: '', ...over,
});

function fakeApi(over: Partial<WorkbenchApi> = {}): WorkbenchApi {
  const notUsed = () => Promise.reject(new Error('not used in this test'));
  return {
    listWorkflows: notUsed, createWorkflow: notUsed, deleteWorkflow: notUsed, archiveWorkflow: notUsed,
    unarchiveWorkflow: notUsed, listVersions: notUsed, startRun: notUsed, listRuns: notUsed, getRun: notUsed,
    listRunEvents: notUsed, listRunVariables: notUsed, getRunTimeline: notUsed, cancelRun: notUsed, retryRun: notUsed,
    listGates: notUsed, decideGate: notUsed,
    getWorkflow: vi.fn(async () => definition()),
    saveWorkflow: vi.fn(async (_id: string, yamlContent: string) => definition({ yamlContent })),
    publishWorkflow: vi.fn(async () => ({ id: 'v1', workflowDefinitionId: 'wf-1', commitSha: 'abc', tagName: 'wf-1/v1', branchName: 'main', message: 'Published', isProduction: true, createdAt: '' })),
    ...over,
  };
}

// The YAML editor (CodeMirror) and the graph preview (Cytoscape) are stand-ins here; each has its own tests.
const stubs = {
  FmYamlEditor: {
    props: ['modelValue', 'readOnly'],
    emits: ['update:modelValue'],
    template: '<textarea data-testid="yaml" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  WorkflowGraphPreview: { props: ['yaml'], template: '<div />' },
};

const mountPage = (api: WorkbenchApi, props: Record<string, unknown> = {}) => mount(WorkflowEditorPage, {
  props: { workflowId: 'wf-1', ...props },
  global: { provide: { [WORKBENCH_HOST as symbol]: { api } }, stubs },
});

describe('WorkflowEditorPage', () => {
  it('saves an edit and says so', async () => {
    const api = fakeApi();
    const wrapper = mountPage(api);
    await flushPromises();
    expect(wrapper.get('[data-testid="editor-save"]').attributes('disabled')).toBeDefined();

    await wrapper.get('[data-testid="yaml"]').setValue(YAML.replace('Leave request', 'Holiday request'));
    expect(wrapper.find('[data-testid="editor-unsaved"]').exists()).toBe(true);
    await wrapper.get('[data-testid="editor-save"]').trigger('click');
    await flushPromises();

    expect(api.saveWorkflow).toHaveBeenCalledWith('wf-1', expect.stringContaining('Holiday request'));
    expect(wrapper.find('[data-testid="editor-unsaved"]').exists()).toBe(false);
    expect(wrapper.text()).toContain('Saved.');
  });

  it('saves before publishing and reports the version', async () => {
    const api = fakeApi({ getWorkflow: vi.fn().mockResolvedValueOnce(definition()).mockResolvedValue(definition({ status: 'Published' })) });
    const wrapper = mountPage(api);
    await flushPromises();
    await wrapper.get('[data-testid="yaml"]').setValue(`${YAML}# note\n`);
    await wrapper.get('[data-testid="editor-publish"]').trigger('click');
    await flushPromises();

    expect(api.saveWorkflow).toHaveBeenCalled();
    expect(api.publishWorkflow).toHaveBeenCalledWith('wf-1');
    expect(wrapper.text()).toContain('Published as wf-1/v1');
    expect(wrapper.emitted('published')).toHaveLength(1);
  });

  it("shows the server's reason when publishing is refused and keeps the edit", async () => {
    const api = fakeApi({
      publishWorkflow: vi.fn(async () => { throw new WorkbenchApiError("Edge 'e1' points to 'nowhere', which isn't a node. Fix the edge and publish again.", 'SFG_PARSE_ERROR', 422); }),
    });
    const wrapper = mountPage(api);
    await flushPromises();
    await wrapper.get('[data-testid="editor-publish"]').trigger('click');
    await flushPromises();
    expect(wrapper.get('[role="alert"]').text()).toContain("Not published: Edge 'e1' points to 'nowhere'");
  });

  it('keeps the edit when saving fails', async () => {
    const api = fakeApi({ saveWorkflow: vi.fn(async () => { throw new WorkbenchApiError('Could not reach the Flowamaz server.', 'NETWORK_ERROR'); }) });
    const wrapper = mountPage(api);
    await flushPromises();
    await wrapper.get('[data-testid="yaml"]').setValue(`${YAML}# draft\n`);
    await wrapper.get('[data-testid="editor-save"]').trigger('click');
    await flushPromises();
    expect(wrapper.get('[role="alert"]').text()).toContain('Your changes are still here');
    expect(wrapper.find('[data-testid="editor-unsaved"]').exists()).toBe(true);
  });

  it('read-only shows no Save or Publish, and a missing workflow offers the way back', async () => {
    const readOnly = mountPage(fakeApi(), { readOnly: true });
    await flushPromises();
    expect(readOnly.find('[data-testid="editor-save"]').exists()).toBe(false);

    const missing = mountPage(fakeApi({ getWorkflow: vi.fn(async () => { throw new WorkbenchApiError("No workflow 'wf-1' in this workspace.", 'WORKFLOW_NOT_FOUND', 404); }) }));
    await flushPromises();
    expect(missing.text()).toContain('Workflow not found');
    await missing.get('button').trigger('click');
    expect(missing.emitted('back')).toHaveLength(1);
  });
});
