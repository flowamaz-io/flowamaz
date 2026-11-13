import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import type { ConversationPreviewResult } from '@/services/creation.service';

const push = vi.fn();
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }));

const previewConversation = vi.fn();
const createWorkflow = vi.fn();
vi.mock('@/services/creation.service', () => ({
  previewConversation: (...args: unknown[]) => previewConversation(...args),
  createWorkflow: (...args: unknown[]) => createWorkflow(...args),
}));

import ConversationImportPanel from '@/components/creation/ConversationImportPanel.vue';

const preview = (): ConversationPreviewResult => ({
  extraction: {
    workflowName: 'Purchase approval',
    summary: 'Approves purchases',
    trigger: 'A request is submitted',
    participants: [{ name: 'Anita', messageCount: 2 }, { name: 'Rajan', messageCount: 1 }],
    steps: [
      { title: 'Submit request', owner: 'Anita', system: null, saidBy: 'Anita' },
      { title: 'Approve', owner: 'Rajan', system: null, saidBy: 'Rajan' },
    ],
    decisions: [{ condition: 'amount above $5,000', outcome: 'CFO approves', saidBy: 'Rajan' }],
    approvers: [{ who: 'Rajan', when: 'up to $5,000' }],
    agreements: ['Rajan approves up to $5,000'],
    systems: ['SAP'],
    openQuestions: [],
    confidence: 0.45,
  },
  warnings: ['Left undecided in the thread: who covers for the CFO?'],
  tokensUsed: 500,
});

async function extract(name = '') {
  const wrapper = mount(ConversationImportPanel, { props: { workspaceId: 'ws-1', workflowName: name } });
  await wrapper.get('[role="radio"][aria-checked="false"]').trigger('click'); // Slack
  await wrapper.get('textarea').setValue('Anita: please approve\nRajan: ok up to $5k');
  await wrapper.findAll('button').find((b) => b.text() === 'Extract process')!.trigger('click');
  await flushPromises();
  return wrapper;
}

describe('ConversationImportPanel', () => {
  beforeEach(() => {
    previewConversation.mockReset().mockResolvedValue(preview());
    createWorkflow.mockReset().mockResolvedValue({ workflowId: 'wf-1' });
    push.mockReset();
  });

  it('extracts for review first and saves nothing until Generate', async () => {
    const wrapper = await extract();

    expect(previewConversation).toHaveBeenCalledWith('ws-1', 'Anita: please approve\nRajan: ok up to $5k', 'slack');
    expect(createWorkflow).not.toHaveBeenCalled();
    expect(wrapper.findAll('[data-test="conversation-step"]')).toHaveLength(2);
    expect(wrapper.text()).toContain('45% confident');
    expect(wrapper.text()).toContain('Left undecided in the thread: who covers for the CFO?');
    expect(wrapper.text()).toContain('Anita · 2 messages');
  });

  it('generates from the edited extraction, not the original', async () => {
    const wrapper = await extract();

    await wrapper.get('input[aria-label="Step 2"]').setValue('Approve or reject');
    await wrapper.get('button[aria-label="Remove step 1"]').trigger('click');
    await wrapper.get('[data-test="create-from-conversation"]').trigger('click');
    await flushPromises();

    const req = createWorkflow.mock.calls[0][1] as { name: string; method: string; conversationExtraction: { steps: { title: string }[] }; conversationText?: string };
    expect(req.method).toBe('conversation');
    expect(req.name).toBe('Purchase approval');
    expect(req.conversationText).toBeUndefined();
    expect(req.conversationExtraction.steps.map((s) => s.title)).toEqual(['Approve or reject']);
    expect(push).toHaveBeenCalledWith({ name: 'workflow-editor', params: { id: 'wf-1' }, query: { workspaceId: 'ws-1' } });
  });

  it('blocks Generate when every step is removed, and says what to do', async () => {
    const wrapper = await extract();

    await wrapper.get('button[aria-label="Remove step 2"]').trigger('click');
    await wrapper.get('button[aria-label="Remove step 1"]').trigger('click');

    expect(wrapper.get('[data-test="create-from-conversation"]').attributes('disabled')).toBeDefined();
    expect(wrapper.text()).toContain('Every step is removed. Add at least one step');
  });

  it('shows the server error and keeps the pasted text when extraction fails', async () => {
    previewConversation.mockRejectedValue(new Error('No process steps were found in this conversation, so no workflow was generated.'));

    const wrapper = await extract();

    expect(wrapper.get('[role="alert"]').text()).toContain('No process steps were found');
    expect((wrapper.get('textarea').element as HTMLTextAreaElement).value).toContain('Anita: please approve');
  });

  it('a name typed above wins over the extracted one', async () => {
    const wrapper = await extract('My approvals');

    await wrapper.get('[data-test="create-from-conversation"]').trigger('click');
    await flushPromises();

    expect((createWorkflow.mock.calls[0][1] as { name: string }).name).toBe('My approvals');
  });
});
