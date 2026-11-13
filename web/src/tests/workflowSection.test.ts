import { describe, expect, it } from 'vitest';
import * as jsYaml from 'js-yaml';
import { workflowSection } from '@/utils/workflowGraph.util';
import { findNodeIdLine } from '@/utils/canvas.util';

// Short-shape workflows (top-level nodes:) were given a second spec.nodes on save (CC, PR #23).
const SHORT = [
  'workflow: { id: w, version: v1, name: W }',
  'nodes:',
  '  - { id: start, type: Trigger }',
  '  - id: approve',
  '    type: HumanGate',
  'edges:',
  '  - { id: e1, from: start, to: approve }',
].join('\n');

describe('workflowSection', () => {
  it('returns spec in flowamaz/v1 and the document itself in the short shape', () => {
    const v1 = { spec: { nodes: [] } };
    expect(workflowSection(v1)).toBe(v1.spec);
    const short = jsYaml.load(SHORT) as Record<string, unknown>;
    expect(workflowSection(short)).toBe(short);
  });

  it('writes into the short shape in place, never adding spec', () => {
    const doc = jsYaml.load(SHORT) as Record<string, unknown>;
    const section = workflowSection(doc, true);
    (section.nodes as Record<string, unknown>[])[1].config = { form: { fields: [] } };
    expect(doc.spec).toBeUndefined();
    const out = jsYaml.dump(doc);
    expect(out.match(/id: approve/g)).toHaveLength(1);
  });

  it('creates spec only for a document with neither', () => {
    const doc: Record<string, unknown> = { metadata: { id: 'x' } };
    expect(workflowSection(doc)).toBeNull();
    expect(workflowSection(doc, true)).toBe(doc.spec);
  });
});

describe('findNodeIdLine', () => {
  it('finds nodes in the short shape too', () => {
    expect(findNodeIdLine(SHORT, 'approve')).toBe(4);
    expect(findNodeIdLine(SHORT, 'start')).toBe(3);
    expect(findNodeIdLine(SHORT, 'e1')).toBeNull();
  });
});
