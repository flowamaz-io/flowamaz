import { describe, it, expect } from 'vitest';
import * as jsYaml from 'js-yaml';
import { buildCanvasSkeleton, dumpWorkflowYaml, expandYamlAliases } from '@/utils/yaml.util';

const hasAnchorsOrAliases = (text: string) => /(^|[\s:[,-])[&*][\w-]+/m.test(text);

describe('yaml.util', () => {
  it('dumps a fresh canvas skeleton without anchors or aliases', () => {
    const text = dumpWorkflowYaml(buildCanvasSkeleton('My Flow'));
    expect(text).not.toContain('&');
    expect(text).not.toContain('*');
    expect(text).toContain('nodes: []');
    expect(text).toContain('edges: []');
  });

  it('gives every skeleton its own nodes and edges arrays', () => {
    const spec = buildCanvasSkeleton('x').spec as { nodes: unknown[]; edges: unknown[] };
    expect(spec.nodes).not.toBe(spec.edges);
  });

  it('never emits aliases even when the same instance appears twice', () => {
    const shared: unknown[] = [];
    const text = dumpWorkflowYaml({ spec: { nodes: shared, edges: shared } });
    expect(hasAnchorsOrAliases(text)).toBe(false);
  });

  it('expands a server skeleton that aliased nodes and edges', () => {
    const server = 'apiVersion: flowamaz/v1\nspec:\n  nodes: &o0 []\n  edges: *o0\n';
    const expanded = expandYamlAliases(server);
    expect(hasAnchorsOrAliases(expanded)).toBe(false);
    const doc = jsYaml.load(expanded) as { spec: { nodes: unknown[]; edges: unknown[] } };
    expect(doc.spec.nodes).not.toBe(doc.spec.edges);
  });

  it('leaves alias-free YAML byte-for-byte unchanged (comments kept)', () => {
    const text = '# keep me\nspec:\n  nodes: []  # note\n  edges: []\n  label: "a*b & c"\n';
    expect(expandYamlAliases(text)).toBe(text);
  });
});
