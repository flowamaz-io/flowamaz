import { describe, it, expect, vi, beforeEach } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';

const push = vi.fn();
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }));

const clarifyNl = vi.fn();
const createWorkflow = vi.fn();
vi.mock('@/services/creation.service', () => ({
  clarifyNl: (...args: unknown[]) => clarifyNl(...args),
  createWorkflow: (...args: unknown[]) => createWorkflow(...args),
}));

import NlTemplateForm from '@/components/creation/NlTemplateForm.vue';

async function fillAndSubmit() {
  const wrapper = mount(NlTemplateForm, {
    props: { workspaceId: 'ws-1' },
    global: { stubs: { VoiceInputButton: true } },
  });
  await wrapper.get('input').setValue('Expense claims');
  const areas = wrapper.findAll('textarea');
  await areas[0].setValue('Handle expense claims');
  await areas[2].setValue('Check the claim.\nIt needs approval.\nPay the claim.');
  await wrapper.get('[data-test="generate"]').trigger('click');
  await flushPromises();
  return wrapper;
}

describe('NlTemplateForm clarifying questions', () => {
  beforeEach(() => {
    clarifyNl.mockReset();
    createWorkflow.mockReset().mockResolvedValue({ workflowId: 'wf-1' });
    push.mockReset();
  });

  it('generates immediately when no questions are needed (unchanged behaviour)', async () => {
    clarifyNl.mockResolvedValue({ questions: [], usedAi: false, tokensUsed: 0 });

    const wrapper = await fillAndSubmit();

    expect(wrapper.find('[data-test="clarifying-questions"]').exists()).toBe(false);
    expect(createWorkflow).toHaveBeenCalledTimes(1);
    const req = createWorkflow.mock.calls[0][1] as { nlRequest: { clarifications?: unknown } };
    expect(req.nlRequest.clarifications).toBeUndefined();
    expect(push).toHaveBeenCalled();
  });

  it('shows questions, then sends only the answered ones with generation', async () => {
    clarifyNl.mockResolvedValue({
      questions: [
        { id: 'q-approver', topic: 'approver', question: 'Who approves this request?', hint: 'e.g. Finance team' },
        { id: 'q-rejection', topic: 'rejection', question: 'What should happen if the request is rejected?' },
      ],
      usedAi: false,
      tokensUsed: 0,
    });

    const wrapper = await fillAndSubmit();

    expect(wrapper.get('[data-test="clarifying-questions"]').text()).toContain('Who approves this request?');
    expect(createWorkflow).not.toHaveBeenCalled();

    await wrapper.get('[data-test="answer-q-approver"]').setValue('Line manager');
    await wrapper.get('[data-test="generate-with-answers"]').trigger('click');
    await flushPromises();

    expect(createWorkflow).toHaveBeenCalledTimes(1);
    const req = createWorkflow.mock.calls[0][1] as { nlRequest: { clarifications?: unknown } };
    expect(req.nlRequest.clarifications).toEqual([
      { topic: 'approver', question: 'Who approves this request?', answer: 'Line manager' },
    ]);
  });

  it('skip generates without clarifications', async () => {
    clarifyNl.mockResolvedValue({
      questions: [{ id: 'q-trigger', topic: 'trigger', question: 'What starts this workflow?' }],
      usedAi: false,
      tokensUsed: 0,
    });

    const wrapper = await fillAndSubmit();
    await wrapper.get('[data-test="skip-questions"]').trigger('click');
    await flushPromises();

    const req = createWorkflow.mock.calls[0][1] as { nlRequest: { clarifications?: unknown } };
    expect(req.nlRequest.clarifications).toBeUndefined();
  });

  it('falls back to generating when the clarify check itself fails', async () => {
    clarifyNl.mockRejectedValue(new Error('network down'));

    await fillAndSubmit();

    expect(createWorkflow).toHaveBeenCalledTimes(1);
  });
});
