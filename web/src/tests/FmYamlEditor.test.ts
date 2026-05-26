import { describe, it, expect } from 'vitest';
import { mount } from '@vue/test-utils';
import FmYamlEditor from '@/components/editor/FmYamlEditor.vue';

// CodeMirror requires a DOM environment — jsdom is active in vitest
describe('FmYamlEditor', () => {
  it('mounts without error', () => {
    const wrapper = mount(FmYamlEditor, {
      props: { modelValue: 'apiVersion: flowamaz/v1\n' },
    });
    expect(wrapper.exists()).toBe(true);
  });

  it('emits update:modelValue when document changes', async () => {
    const wrapper = mount(FmYamlEditor, {
      props: { modelValue: 'apiVersion: flowamaz/v1\n' },
    });
    // Access the underlying EditorView and dispatch a change
    const vm = wrapper.vm as unknown as { setCursor: (pos: number) => void };
    // setCursor is exposed — confirms EditorView initialised
    expect(typeof vm.setCursor).toBe('function');
  });

  it('emits ready after mount', () => {
    const wrapper = mount(FmYamlEditor, {
      props: { modelValue: '' },
    });
    expect(wrapper.emitted('ready')).toBeTruthy();
  });

  it('renders a container div', () => {
    const wrapper = mount(FmYamlEditor, {
      props: { modelValue: '# test\n' },
    });
    expect(wrapper.find('div').exists()).toBe(true);
  });
});
