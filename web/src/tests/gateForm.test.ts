import { describe, expect, it, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import GateFormEditor from '@/components/canvas/config/GateFormEditor.vue';
import GateApproveDialog from '@/components/gates/GateApproveDialog.vue';
import {
  answerProblems, answersToSend, definitionProblems, keyFromLabel,
} from '@/utils/gateForm.util';
import type { GateFormField } from '@/types';

vi.mock('@/services/library.service', () => ({
  libraryService: {
    listForms: vi.fn(async () => [{
      id: 'f1', slug: 'purchase-details', name: 'Purchase details', description: '', category: 'Finance', version: '1.0.0',
      publisher: 'flowamaz-io', isOfficial: true, isWorkspaceForm: false, tags: [], deprecatedAt: null, deprecationNote: null,
      fields: [{ key: 'approved_amount', label: 'Approved amount', type: 'number', required: true, min: 0 }],
    }]),
    saveForm: vi.fn(async () => {
      throw Object.assign(new Error('Request failed with status code 409'), {
        isAxiosError: true,
        response: { status: 409, data: { code: 'LIBRARY_FORM_NAME_TAKEN', message: "This workspace already has a form called 'Vendor check'." } },
      });
    }),
  },
}));

const FIELDS: GateFormField[] = [
  { key: 'amount', label: 'Amount', type: 'number', required: true, min: 0, max: 100 },
  { key: 'line', label: 'Budget line', type: 'select', options: ['Opex', 'Capex'] },
  { key: 'ok', label: 'Receipts checked', type: 'checkbox', required: true },
  { key: 'on', label: 'Start', type: 'date' },
];

describe('gate form rules', () => {
  it('match the server wording for each problem', () => {
    expect(answerProblems(FIELDS, { amount: '150', line: 'Travel', ok: false, on: '1/2/2026' })).toEqual({
      amount: 'Amount must be at most 100.',
      line: 'Budget line must be one of: Opex, Capex.',
      ok: 'Receipts checked must be ticked to approve.',
      on: 'Start must be a date (YYYY-MM-DD).',
    });
    expect(answerProblems(FIELDS, { amount: '', line: '', ok: true, on: '' })).toEqual({ amount: 'Amount is required.' });
  });

  it('sends numbers as numbers and leaves blanks out', () => {
    expect(answersToSend(FIELDS, { amount: ' 42 ', line: '', ok: true, on: '2026-12-01' })).toEqual({ amount: 42, ok: true, on: '2026-12-01' });
  });

  it('checks definitions and derives keys', () => {
    expect(keyFromLabel('Cost centre (EU)')).toBe('cost_centre_eu');
    expect(keyFromLabel('2nd approver')).toBe('nd_approver');
    expect(definitionProblems([{ key: 'a', label: 'A', type: 'select' }, { key: 'a', label: 'B', type: 'text' }])).toEqual([
      'A: add at least one choice.',
      "B: the key 'a' is used twice. Give each field its own key.",
    ]);
  });
});

describe('GateFormEditor', () => {
  it('adds a field as config.form.fields and keys follow the label', async () => {
    const wrapper = mount(GateFormEditor, { props: { config: { assignee: 'Admin' } } });
    expect(wrapper.find('[data-test="gate-form-empty"]').exists()).toBe(true);
    await wrapper.find('[data-test="gate-form-add"]').trigger('click');
    const added = wrapper.emitted('update')![0][0] as Record<string, unknown>;
    expect(added).toEqual({ assignee: 'Admin', form: { fields: [{ key: 'field_1', label: 'Field 1', type: 'text' }] } });

    await wrapper.setProps({ config: { form: { fields: [{ key: 'field_1', label: 'Field 1', type: 'text' }] } } });
    await wrapper.find('input[aria-label="Field label"]').setValue('Cost centre');
    expect(wrapper.emitted('update')!.at(-1)![0]).toEqual({ form: { fields: [{ key: 'cost_centre', label: 'Cost centre', type: 'text' }] } });
  });

  it('copies a Library form into the gate', async () => {
    const wrapper = mount(GateFormEditor, { props: { config: {}, workspaceId: 'w1' } });
    await wrapper.find('[data-test="gate-form-library"]').trigger('click');
    await flushPromises();
    await wrapper.find('[data-test="gate-form-use-purchase-details"]').trigger('click');
    expect(wrapper.emitted('update')!.at(-1)![0]).toEqual({
      form: { fields: [{ key: 'approved_amount', label: 'Approved amount', type: 'number', required: true, min: 0 }] },
    });
  });

  it("shows the server's message when saving fails", async () => {
    const wrapper = mount(GateFormEditor, { props: { config: { form: { fields: [{ key: 'a', label: 'A', type: 'text' }] } }, workspaceId: 'w1' } });
    await wrapper.find('[data-test="gate-form-save"]').trigger('click');
    await wrapper.find('input[aria-label="Form name"]').setValue('Vendor check');
    await wrapper.find('form').trigger('submit');
    await flushPromises();
    expect(wrapper.text()).toContain("already has a form called 'Vendor check'");
    expect(wrapper.text()).not.toContain('status code 409');
  });

  it('removing the last field removes the form', async () => {
    const wrapper = mount(GateFormEditor, { props: { config: { x: 1, form: { fields: [{ key: 'a', label: 'A', type: 'text' }] } } } });
    await wrapper.find('button[aria-label="Remove A"]').trigger('click');
    expect(wrapper.emitted('update')!.at(-1)![0]).toEqual({ x: 1 });
  });
});

describe('GateApproveDialog', () => {
  it('only approves once the answers are valid', async () => {
    const wrapper = mount(GateApproveDialog, {
      props: { modelValue: true, gateName: 'Manager approval', fields: FIELDS, submitting: false, serverError: null },
      attachTo: document.body,
    });
    const submit = (): HTMLButtonElement => document.querySelector('[data-testid="gate-approve-submit"]') as HTMLButtonElement;
    submit().click();
    await flushPromises();
    expect(wrapper.emitted('approve')).toBeUndefined();
    expect(document.body.textContent).toContain('Amount is required.');

    const amount = document.querySelector('#gate-field-input-amount') as HTMLInputElement;
    amount.value = '80';
    amount.dispatchEvent(new Event('input'));
    await flushPromises();
    const tick = document.querySelector('[data-testid="gate-field-ok"] input') as HTMLInputElement;
    tick.checked = true;
    tick.dispatchEvent(new Event('change'));
    await flushPromises();
    submit().click();
    await flushPromises();
    expect(wrapper.emitted('approve')![0]).toEqual([{ amount: 80, ok: true }, undefined]);
    wrapper.unmount();
  });
});
