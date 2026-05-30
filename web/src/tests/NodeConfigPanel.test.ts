import { describe, it, expect } from 'vitest';
import { mount } from '@vue/test-utils';
import NodeConfigPanel, { type ConfigNode } from '@/components/canvas/NodeConfigPanel.vue';
import RouterConditionBuilder from '@/components/canvas/config/RouterConditionBuilder.vue';
import VariableAutocomplete from '@/components/canvas/shared/VariableAutocomplete.vue';

function mountPanel(node: ConfigNode) {
  return mount(NodeConfigPanel, {
    props: { open: true, node, workspaceId: 'ws-1', variables: [] },
    global: { stubs: { RouterLink: true } },
  });
}

describe('NodeConfigPanel', () => {
  it('trigger type change updates the saved config', async () => {
    const node: ConfigNode = { id: 'trigger-1', type: 'trigger', label: 'Start', config: { triggerType: 'manual' } };
    const wrapper = mountPanel(node);

    await wrapper.find('select').setValue('schedule');
    await wrapper.get('button[class*="bg-indigo-500"]').trigger('click');

    const saved = wrapper.emitted('save')?.[0]?.[0] as { config: Record<string, unknown> };
    expect(saved.config.triggerType).toBe('schedule');
  });

  it('human-gate assignee type change renders the matching field', async () => {
    const node: ConfigNode = {
      id: 'gate-1', type: 'human-gate', label: 'Approval', config: { assigneeType: 'role' },
    };
    const wrapper = mountPanel(node);

    // First select on the human-gate Basic tab is the assignee type.
    await wrapper.find('select').setValue('variable');

    const variableInput = wrapper.findAll('input').find((i) => i.attributes('placeholder')?.includes('approver_email'));
    expect(variableInput).toBeTruthy();
  });

  it('save merges config without losing other fields', async () => {
    const node: ConfigNode = {
      id: 'trigger-1', type: 'trigger', label: 'Start',
      config: { triggerType: 'manual', keepMe: 'value' },
    };
    const wrapper = mountPanel(node);

    await wrapper.find('select').setValue('schedule');
    await wrapper.get('button[class*="bg-indigo-500"]').trigger('click');

    const saved = wrapper.emitted('save')?.[0]?.[0] as { label: string; config: Record<string, unknown> };
    expect(saved.config.keepMe).toBe('value');
    expect(saved.config.triggerType).toBe('schedule');
    expect(saved.label).toBe('Start');
  });
});

describe('RouterConditionBuilder', () => {
  it('add branch appends a branch with an empty condition', async () => {
    const wrapper = mount(RouterConditionBuilder, {
      props: { config: { branches: [] }, variables: [] },
    });

    await wrapper.get('[data-test="add-branch"]').trigger('click');

    const updated = wrapper.emitted('update')?.[0]?.[0] as { branches: Array<{ label: string; condition: string }> };
    expect(updated.branches).toHaveLength(1);
    expect(updated.branches[0]!.condition).toBe('');
    expect(updated.branches[0]!.label).toContain('Branch');
  });
});

describe('VariableAutocomplete', () => {
  it('shows variables matching the open ${ fragment', async () => {
    const wrapper = mount(VariableAutocomplete, {
      props: {
        modelValue: '${ap',
        variables: [
          { name: 'approver_email', type: 'string' },
          { name: 'amount', type: 'number' },
        ],
      },
    });

    await wrapper.find('input').trigger('focus');

    const suggestions = wrapper.findAll('[data-test="variable-suggestion"]');
    expect(suggestions).toHaveLength(1);
    expect(suggestions[0]!.text()).toContain('approver_email');
  });
});
