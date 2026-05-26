import { describe, it, expect } from 'vitest';
import { mount } from '@vue/test-utils';
import CopilotPanel from '@/components/editor/CopilotPanel.vue';

describe('CopilotPanel', () => {
  it('shows input and toolbar when open', () => {
    const wrapper = mount(CopilotPanel, {
      props: { open: true, history: [] },
    });
    expect(wrapper.find('input').exists()).toBe(true);
    expect(wrapper.text()).toContain('Co-pilot');
  });

  it('does not show inner content when closed', () => {
    const wrapper = mount(CopilotPanel, {
      props: { open: false, history: [] },
    });
    // When closed the v-if inside Transition is false — no input
    expect(wrapper.find('input').exists()).toBe(false);
  });

  it('emits close on Escape key', async () => {
    const wrapper = mount(CopilotPanel, {
      props: { open: true, history: [] },
    });
    const input = wrapper.find('input');
    await input.trigger('keydown', { key: 'Escape' });
    expect(wrapper.emitted('close')).toBeTruthy();
  });

  it('emits command when Enter is pressed with non-empty input', async () => {
    const wrapper = mount(CopilotPanel, {
      props: { open: true, history: [] },
    });
    await wrapper.find('input').setValue('add timeout 30m');
    await wrapper.find('input').trigger('keydown', { key: 'Enter' });
    const emitted = wrapper.emitted('command') as string[][];
    expect(emitted?.some(e => e[0] === 'add timeout 30m')).toBe(true);
  });

  it('shows history items as suggestion buttons', () => {
    const wrapper = mount(CopilotPanel, {
      props: { open: true, history: ['rename step', 'add timeout 24h', 'remove step'] },
    });
    expect(wrapper.text()).toContain('Recent commands');
    expect(wrapper.text()).toContain('rename step');
  });

  it('shows result after setResult called', async () => {
    const wrapper = mount(CopilotPanel, {
      props: { open: true, history: [] },
    });
    (wrapper.vm as InstanceType<typeof CopilotPanel>).setResult('patch: value', 'add-timeout');
    await wrapper.vm.$nextTick();
    expect(wrapper.text()).toContain('add-timeout');
  });
});
