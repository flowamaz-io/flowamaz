import { describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';
import FmRunTimeline from '../src/components/FmRunTimeline.vue';
import type { TimelineResponse } from '../src/types';

const timeline: TimelineResponse = {
  instanceId: 'i1',
  totalDurationMs: 100,
  startedAt: null,
  completedAt: null,
  nodes: [
    { nodeId: 'a', nodeType: 'Trigger', label: 'Start', status: 'Completed', startedAt: null, completedAt: null, durationMs: 40, offsetMs: 0, retryCount: 0, hasOutput: true },
    { nodeId: 'b', nodeType: 'Action', label: 'Call API', status: 'Running', startedAt: null, completedAt: null, durationMs: 60, offsetMs: 40, retryCount: 0, hasOutput: false },
  ],
};

describe('FmRunTimeline', () => {
  it('positions bars relative to total duration', () => {
    const wrapper = mount(FmRunTimeline, { props: { timeline } });

    const barA = wrapper.find('[data-node-id="a"]');
    const barB = wrapper.find('[data-node-id="b"]');

    expect(barA.attributes('style')).toContain('width: 40%');
    expect(barA.attributes('style')).toContain('left: 0%');
    expect(barB.attributes('style')).toContain('width: 60%');
    expect(barB.attributes('style')).toContain('left: 40%');
  });

  it('renders a row per node with its label', () => {
    const wrapper = mount(FmRunTimeline, { props: { timeline } });
    expect(wrapper.text()).toContain('Start');
    expect(wrapper.text()).toContain('Call API');
  });

  it('handles zero total duration without dividing by zero', () => {
    const empty: TimelineResponse = { ...timeline, totalDurationMs: 0, nodes: [{ ...timeline.nodes[0]!, durationMs: 0, offsetMs: 0 }] };
    const wrapper = mount(FmRunTimeline, { props: { timeline: empty } });
    expect(wrapper.find('[data-node-id="a"]').attributes('style')).toContain('width: 0%');
  });

  it('keeps every bar inside the card when steps start late (a gate wait, retry backoff)', () => {
    // Durations sum to 2s, but the second step starts 10s into the run and runs 15.7s.
    const late: TimelineResponse = {
      ...timeline,
      totalDurationMs: 2000,
      nodes: [
        { ...timeline.nodes[0]!, nodeId: 'a', durationMs: 300, offsetMs: 0 },
        { ...timeline.nodes[1]!, nodeId: 'b', status: 'Failed', durationMs: 15_700, offsetMs: 10_000 },
      ],
    };
    const wrapper = mount(FmRunTimeline, { props: { timeline: late } });

    const style = wrapper.find('[data-node-id="b"]').attributes('style') ?? '';
    const left = Number(/left: ([\d.]+)%/.exec(style)?.[1]);
    const width = Number(/width: ([\d.]+)%/.exec(style)?.[1]);
    expect(left + width).toBeLessThanOrEqual(100.0001);
    expect(wrapper.text()).toContain('15.7s');
  });
});
