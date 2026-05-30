import { describe, expect, it, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import DeleteWorkflowModal from '@/components/workflow/DeleteWorkflowModal.vue';

const push = vi.fn();
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
}));

const remove = vi.fn();
vi.mock('@/services/workflow.service', () => ({
  workflowService: { remove: (...args: unknown[]) => remove(...args) },
}));

const success = vi.fn();
vi.mock('@/composables/useToast', () => ({
  useToast: () => ({ success, error: vi.fn() }),
}));

function mountModal() {
  return mount(DeleteWorkflowModal, {
    props: { modelValue: true, workspaceId: 'ws1', workflowId: 'wf1', workflowName: 'Orders' },
    global: { stubs: { Teleport: true } },
  });
}

describe('DeleteWorkflowModal', () => {
  beforeEach(() => {
    push.mockReset();
    remove.mockReset().mockResolvedValue(undefined);
    success.mockReset();
  });

  it('confirm calls the DELETE API and navigates to the workflows list', async () => {
    const wrapper = mountModal();
    const confirm = wrapper.findAll('button').find((b) => b.text().includes('Delete workflow'));
    expect(confirm).toBeTruthy();

    await confirm!.trigger('click');
    await flushPromises();

    expect(remove).toHaveBeenCalledWith('ws1', 'wf1');
    expect(wrapper.emitted('deleted')).toBeTruthy();
    expect(push).toHaveBeenCalledWith('/workflows');
  });
});
