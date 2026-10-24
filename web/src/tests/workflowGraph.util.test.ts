import { describe, expect, it } from 'vitest';
import { parseWorkflowGraph } from '@/utils/workflowGraph.util';

describe('parseWorkflowGraph', () => {
  it('reads flow-style edges, quoted labels, comments and router branches (spec shape)', () => {
    const yaml = `
apiVersion: flowamaz/v1
kind: Workflow
metadata: { id: loan, name: "Loan" }
spec:
  trigger:
    type: form
  variables:
    loan_amount: { type: number, required: true }
  nodes:
    # the form
    - id: start
      type: trigger
      label: "Application submitted"
    - id: limit
      type: router
      label: 'Within 50K?'
      config:
        branches:
          - { label: "Up to 50K", condition: "{{ loan_amount }} <= 50000" }
    - { id: done, type: end, label: Done }
  edges:
    - { id: e1, from: start, to: limit }
    - { id: e2, from: limit, to: done, via: "Up to 50K" }
    - { id: e3, from: start, to: done, condition: "{{ loan_amount }} > 1" }
`;
    const graph = parseWorkflowGraph(yaml)!;
    expect(graph.nodes.map((n) => n.id)).toEqual(['start', 'limit', 'done']);
    expect(graph.nodes[0].label).toBe('Application submitted');
    expect(graph.nodes[1].label).toBe('Within 50K?');
    expect(graph.nodes[1].config).toHaveProperty('branches');
    expect(graph.edges).toEqual([
      { id: 'e1', from: 'start', to: 'limit' },
      { id: 'e2', from: 'limit', to: 'done', via: 'Up to 50K' },
      { id: 'e3', from: 'start', to: 'done', condition: '{{ loan_amount }} > 1' },
    ]);
  });

  it('does not mistake variable or trigger "type:" lines for node types', () => {
    const yaml = `
spec:
  trigger: { type: webhook }
  variables:
    amount:
      type: number
  nodes:
    - id: a
      type: human-gate
      label: Approve
`;
    const graph = parseWorkflowGraph(yaml)!;
    expect(graph.nodes).toHaveLength(1);
    expect(graph.nodes[0].type).toBe('human-gate');
  });

  it('accepts the legacy top-level nodes/edges shape', () => {
    const yaml = `
workflow: { id: bp, version: v1, name: Bp }
nodes:
  - { id: start, type: Trigger }
  - { id: done, type: End }
edges:
  - { id: e1, from: start, to: done }
`;
    const graph = parseWorkflowGraph(yaml)!;
    expect(graph.nodes).toHaveLength(2);
    expect(graph.edges).toEqual([{ id: 'e1', from: 'start', to: 'done' }]);
  });

  it('returns null for invalid YAML or a document without nodes', () => {
    expect(parseWorkflowGraph('spec: [unclosed')).toBeNull();
    expect(parseWorkflowGraph('spec:\n  nodes: []\n')).toBeNull();
  });
});
