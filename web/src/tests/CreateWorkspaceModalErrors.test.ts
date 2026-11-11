import { describe, it, expect, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import CreateWorkspaceModal from '@/components/workspace/CreateWorkspaceModal.vue';

// A workspace URL that's already used is reported under the URL field.

const createWorkspace = vi.fn(async () => {
  throw Object.assign(new Error('taken'), {
    isAxiosError: true,
    response: { status: 409, data: { code: 'WORKSPACE_SLUG_TAKEN', message: "Your organisation already has a workspace with the URL 'lending'." } },
  });
});
vi.mock('@/composables/useWorkspace', () => ({ useWorkspace: () => ({ createWorkspace }) }));
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));

describe('CreateWorkspaceModal', () => {
  it('shows a taken URL under the URL field', async () => {
    setActivePinia(createPinia());
    const wrapper = mount(CreateWorkspaceModal, {
      props: { open: true },
      global: { stubs: { FmModal: { template: '<div><slot /><slot name="footer" /></div>' } } },
    });
    await wrapper.find('input[placeholder="e.g. Finance Ops"]').setValue('Lending');
    await wrapper.findAll('button').find((b) => /create/i.test(b.text()))!.trigger('click');
    await flushPromises();

    const fields = wrapper.findAll('input');
    const urlField = fields.find((i) => i.attributes('placeholder') === 'finance-ops')!;
    const urlBlock = urlField.element.closest('div')!.parentElement!;
    expect(urlBlock.textContent).toContain("already has a workspace with the URL 'lending'");
    const nameBlock = fields.find((i) => i.attributes('placeholder') === 'e.g. Finance Ops')!.element.closest('div')!.parentElement!;
    expect(nameBlock.textContent).not.toContain('already has a workspace');
  });
});
