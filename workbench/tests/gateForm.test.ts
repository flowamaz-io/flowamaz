import { describe, expect, it } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import GateApproveDialog from '../src/components/GateApproveDialog.vue';
import {
  answerProblems, answersToSend, definitionProblems, keyFromLabel,
} from '../src/gateForm';
import type { GateFormField } from '../src/types';

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
