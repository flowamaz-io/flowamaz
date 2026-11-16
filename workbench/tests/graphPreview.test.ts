import { describe, expect, it, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import { ref } from 'vue';
import WorkflowGraphPreview from '../src/components/WorkflowGraphPreview.vue';

// Cytoscape needs a real canvas, which jsdom lacks: the canvas composable is a stand-in here, the YAML parsing is real.
const canvas = { cy: ref({}), setEditable: vi.fn(), loadFromNodes: vi.fn(), runLayout: vi.fn(), fitView: vi.fn() };
vi.mock('@flowamaz/canvas', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@flowamaz/canvas')>()),
  useCytoscapeCanvas: () => canvas,
}));

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

describe('WorkflowGraphPreview', () => {
  it('says whether the YAML can be drawn, waiting for typing to pause', async () => {
    vi.useFakeTimers();
    const wrapper = mount(WorkflowGraphPreview, { props: { yaml: YAML }, attachTo: document.body });
    expect(wrapper.emitted('valid')![0]).toEqual([true]);
    expect(canvas.setEditable).toHaveBeenCalledWith(false);
    expect(canvas.loadFromNodes.mock.calls[0]![0].map((n: { id: string }) => n.id)).toEqual(['start', 'done']);

    await wrapper.setProps({ yaml: 'spec: [' });
    expect(wrapper.emitted('valid')).toHaveLength(1);
    vi.advanceTimersByTime(300);
    expect(wrapper.emitted('valid')![1]).toEqual([false]);
    expect(canvas.loadFromNodes).toHaveBeenCalledTimes(1); // the last good picture stays
    wrapper.unmount();
    vi.useRealTimers();
  });
});
