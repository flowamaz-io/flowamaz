import { describe, it, expect } from 'vitest';
import { mount } from '@vue/test-utils';
import VisualReviewList from '@/components/creation/VisualReviewList.vue';
import type { VisualPreviewResult } from '@/services/creation.service';
import {
  formatConfidence,
  initReview,
  reviewProblems,
  toConfirmedGraph,
  type ReviewItem,
} from '@/utils/visualReview.util';
import { buildClarifications } from '@/utils/clarification.util';

const preview: VisualPreviewResult = {
  nodes: [
    { id: 'n1', label: 'Start', type: 'trigger', confidence: 0.99, reviewNeeded: false, reason: null, annotations: [], suggestedConnectors: [] },
    { id: 'n2', label: 'Chek stok', type: 'action', confidence: 0.62, reviewNeeded: true, reason: 'Handwriting is smudged', annotations: ['SAP'], suggestedConnectors: ['sap-s4hana'] },
    { id: 'n3', label: 'End', type: 'end', confidence: 0.97, reviewNeeded: false, reason: null, annotations: [], suggestedConnectors: [] },
  ],
  edges: [{ from: 'n1', to: 'n2' }, { from: 'n2', to: 'n3', label: null }],
  confidenceThreshold: 0.8,
  costUsd: 0.01,
  tokensUsed: 800,
};

describe('visualReview.util', () => {
  it('initReview keeps every node and copies the original label', () => {
    const state = initReview(preview);
    expect(state.items.every((i) => i.keep)).toBe(true);
    expect(state.items[1].originalLabel).toBe('Chek stok');
    expect(state.edges).toHaveLength(2);
  });

  it('toConfirmedGraph sends kept, renamed nodes and all edges', () => {
    const state = initReview(preview);
    state.items[1] = { ...state.items[1], label: '  Check stock  ' };
    state.items[2] = { ...state.items[2], keep: false };

    const graph = toConfirmedGraph(state);

    expect(graph.nodes.map((n) => n.id)).toEqual(['n1', 'n2']);
    expect(graph.nodes[1]).toEqual({ id: 'n2', label: 'Check stock', type: 'action', suggestedConnectors: ['sap-s4hana'] });
    expect(graph.edges).toEqual([{ from: 'n1', to: 'n2', label: null }, { from: 'n2', to: 'n3', label: null }]);
  });

  it('reviewProblems blocks creation when nothing is kept or a kept step is unnamed', () => {
    const state = initReview(preview);
    expect(reviewProblems(state.items)).toEqual([]);

    const unnamed = state.items.map((i) => (i.id === 'n2' ? { ...i, label: ' ' } : i));
    expect(reviewProblems(unnamed)[0]).toContain('no name');

    const none = state.items.map((i) => ({ ...i, keep: false }));
    expect(reviewProblems(none)[0]).toContain('Keep at least one step');
  });

  it('formatConfidence renders whole percentages and clamps', () => {
    expect(formatConfidence(0.624)).toBe('62%');
    expect(formatConfidence(1.4)).toBe('100%');
  });
});

describe('clarification.util', () => {
  it('buildClarifications drops blank (skipped) answers and trims the rest', () => {
    const qs = [
      { id: 'q-approver', topic: 'approver' as const, question: 'Who approves?' },
      { id: 'q-rejection', topic: 'rejection' as const, question: 'If rejected?' },
    ];
    expect(buildClarifications(qs, { 'q-approver': '  Line manager ', 'q-rejection': '   ' }))
      .toEqual([{ topic: 'approver', question: 'Who approves?', answer: 'Line manager' }]);
    expect(buildClarifications(qs, {})).toEqual([]);
  });
});

describe('VisualReviewList', () => {
  it('lists low-confidence steps first with their confidence and reason', () => {
    const wrapper = mount(VisualReviewList, { props: { items: initReview(preview).items } });
    const rows = wrapper.findAll('li');
    expect(rows[0].attributes('data-test')).toBe('review-item-n2');
    expect(wrapper.get('[data-test="confidence-n2"]').text()).toContain('62%');
    expect(wrapper.get('[data-test="reason-n2"]').text()).toBe('Handwriting is smudged');
    expect(wrapper.find('[data-test="reason-n1"]').exists()).toBe(false);
    expect(wrapper.get('[data-test="review-summary"]').text()).toContain('1 was hard to read');
  });

  it('emits updated items on rename and remove', async () => {
    const items = initReview(preview).items;
    const wrapper = mount(VisualReviewList, { props: { items } });

    await wrapper.get('[data-test="label-n2"]').setValue('Check stock');
    const renamed = wrapper.emitted('update:items')?.at(-1)?.[0] as ReviewItem[];
    expect(renamed.find((i) => i.id === 'n2')?.label).toBe('Check stock');

    await wrapper.setProps({ items: renamed });
    await wrapper.get('[data-test="toggle-n2"]').trigger('click');
    const removed = wrapper.emitted('update:items')?.at(-1)?.[0] as ReviewItem[];
    expect(removed.find((i) => i.id === 'n2')?.keep).toBe(false);
  });
});
