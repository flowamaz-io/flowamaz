import { describe, it, expect, beforeEach } from 'vitest';
import { setActivePinia, createPinia } from 'pinia';
import { useCanvasStore } from '@/stores/canvas.store';
import {
  GRID_SIZE,
  SNAP_PREF_KEY,
  snapToGrid,
  snapPosition,
  loadSnapPreference,
  saveSnapPreference,
  computeMinimapTransform,
  modelToMinimap,
  minimapToModel,
  viewportToMinimapRect,
  panToCenter,
  renderedToModel,
  findNodeIdLine,
  gridDotSpacing,
} from '@/utils/canvas.util';

describe('snapToGrid', () => {
  it('rounds to the nearest 20px by default', () => {
    expect(GRID_SIZE).toBe(20);
    expect(snapToGrid(0)).toBe(0);
    expect(snapToGrid(9)).toBe(0);
    expect(snapToGrid(10)).toBe(20);
    expect(snapToGrid(29)).toBe(20);
    expect(snapToGrid(31)).toBe(40);
    expect(snapToGrid(-11)).toBe(-20);
  });

  it('never returns negative zero', () => {
    expect(Object.is(snapToGrid(-4), 0)).toBe(true);
  });

  it('supports a custom grid and leaves invalid input untouched', () => {
    expect(snapToGrid(26, 25)).toBe(25);
    expect(snapToGrid(13, 0)).toBe(13);
    expect(Number.isNaN(snapToGrid(Number.NaN))).toBe(true);
  });

  it('snaps both axes of a position', () => {
    expect(snapPosition({ x: 47, y: 112.4 })).toEqual({ x: 40, y: 120 });
  });
});

describe('snap preference persistence', () => {
  function memoryStorage() {
    const data = new Map<string, string>();
    return {
      getItem: (k: string) => data.get(k) ?? null,
      setItem: (k: string, v: string) => { data.set(k, v); },
      data,
    };
  }

  it('round-trips through storage', () => {
    const s = memoryStorage();
    expect(loadSnapPreference(s)).toBe(false);
    saveSnapPreference(true, s);
    expect(s.data.get(SNAP_PREF_KEY)).toBe('true');
    expect(loadSnapPreference(s)).toBe(true);
  });

  it('falls back to off when storage throws', () => {
    const throwing = {
      getItem: () => { throw new Error('blocked'); },
      setItem: () => { throw new Error('blocked'); },
    };
    expect(loadSnapPreference(throwing)).toBe(false);
    expect(() => saveSnapPreference(true, throwing)).not.toThrow();
    expect(loadSnapPreference(null)).toBe(false);
  });
});

describe('minimap mapping', () => {
  // 1000 x 500 model box into a 200 x 200 minimap → scale 0.2, letterboxed vertically.
  const t = computeMinimapTransform({ x1: -100, y1: 50, w: 1000, h: 500 }, 200, 200);

  it('fits the bounding box preserving aspect ratio, centred', () => {
    expect(t.scale).toBeCloseTo(0.2);
    expect(t.offsetX).toBeCloseTo(0);
    expect(t.offsetY).toBeCloseTo(50);
  });

  it('maps model → minimap and back', () => {
    expect(modelToMinimap({ x: -100, y: 50 }, t)).toEqual({ x: 0, y: 50 });
    const mm = modelToMinimap({ x: 400, y: 300 }, t);
    expect(mm.x).toBeCloseTo(100);
    expect(mm.y).toBeCloseTo(100);
    const back = minimapToModel(mm, t);
    expect(back.x).toBeCloseTo(400);
    expect(back.y).toBeCloseTo(300);
  });

  it('projects the viewport extent to a rectangle', () => {
    const r = viewportToMinimapRect({ x1: 0, y1: 100, w: 500, h: 250 }, t);
    expect(r.left).toBeCloseTo(20);
    expect(r.top).toBeCloseTo(60);
    expect(r.width).toBeCloseTo(100);
    expect(r.height).toBeCloseTo(50);
  });

  it('guards against an empty bounding box', () => {
    const t0 = computeMinimapTransform({ x1: 10, y1: 10, w: 0, h: 0 }, 100, 80);
    expect(Number.isFinite(t0.scale)).toBe(true);
    expect(Number.isFinite(minimapToModel({ x: 5, y: 5 }, t0).x)).toBe(true);
  });

  it('computes a pan that centres a model point, and inverts rendered → model', () => {
    const pan = panToCenter({ x: 300, y: 200 }, 2, 800, 600);
    expect(pan).toEqual({ x: -200, y: -100 });
    // The viewport centre should map back to the requested model point.
    expect(renderedToModel({ x: 400, y: 300 }, pan, 2)).toEqual({ x: 300, y: 200 });
  });
});

describe('gridDotSpacing', () => {
  it('matches the snap grid at 100% zoom', () => {
    expect(gridDotSpacing(1)).toBe(20);
    expect(gridDotSpacing(2)).toBe(40);
  });

  it('doubles (stays a grid multiple) when zoomed out too far to read', () => {
    expect(gridDotSpacing(0.25)).toBe(10); // 5 → 10
    expect(gridDotSpacing(0.1)).toBe(16); // 2 → 4 → 8 → 16
    expect(gridDotSpacing(0)).toBe(20);
  });
});

describe('findNodeIdLine', () => {
  const blockYaml = [
    'apiVersion: flowamaz/v1',          // 1
    'kind: Workflow',                   // 2
    'metadata:',                        // 3
    '  id: start',                      // 4  metadata id — must not match
    'spec:',                            // 5
    '  trigger:',                       // 6
    '    type: manual',                 // 7
    '  nodes:',                         // 8
    '    - id: start',                  // 9
    '      type: trigger',              // 10
    '      config:',                    // 11
    '        items:',                   // 12
    '          - id: nested',           // 13 nested list — must not match
    '    - type: action',               // 14
    '      id: send-email',             // 15 id as a later key
    '    - id: "quoted id"',            // 16
    "    - id: 'it''s'",                // 17
    '    - id: commented # trailing',   // 18
    '  edges:',                         // 19
    '    - id: e1',                     // 20
    '      from: start',                // 21
    '      to: send-email',             // 22
  ].join('\n');

  it('finds `- id: x` items inside spec.nodes', () => {
    expect(findNodeIdLine(blockYaml, 'start')).toBe(9);
  });

  it('finds an id that is not the first key of its item', () => {
    expect(findNodeIdLine(blockYaml, 'send-email')).toBe(15);
  });

  it('handles double- and single-quoted ids and trailing comments', () => {
    expect(findNodeIdLine(blockYaml, 'quoted id')).toBe(16);
    expect(findNodeIdLine(blockYaml, "it's")).toBe(17);
    expect(findNodeIdLine(blockYaml, 'commented')).toBe(18);
  });

  it('ignores ids in nested config, edges and metadata', () => {
    expect(findNodeIdLine(blockYaml, 'nested')).toBeNull();
    expect(findNodeIdLine(blockYaml, 'e1')).toBeNull();
    expect(findNodeIdLine(blockYaml, 'missing')).toBeNull();
  });

  it('does not match on id prefixes', () => {
    expect(findNodeIdLine(blockYaml, 'send')).toBeNull();
  });

  it('handles flow-style list items', () => {
    const yaml = [
      'spec:',
      '  nodes:',
      '    - { type: action, config: { id: inner }, id: a1 }',
      '    - {id: "b 2", type: end}',
      '  edges: []',
    ].join('\n');
    expect(findNodeIdLine(yaml, 'a1')).toBe(3);
    expect(findNodeIdLine(yaml, 'b 2')).toBe(4);
    expect(findNodeIdLine(yaml, 'inner')).toBeNull();
  });

  it('handles a flow sequence for nodes', () => {
    const yaml = [
      'spec:',
      '  nodes: [',
      '    { id: one, type: trigger },',
      '    { id: two, type: end }',
      '  ]',
      '  edges: [{ id: one-two, from: one, to: two }]',
    ].join('\n');
    expect(findNodeIdLine(yaml, 'one')).toBe(3);
    expect(findNodeIdLine(yaml, 'two')).toBe(4);
    expect(findNodeIdLine(yaml, 'one-two')).toBeNull();
  });

  it('handles compact sequences at the same indent as `nodes:` and CRLF line endings', () => {
    const yaml = ['spec:', '  nodes:', '  - id: x', '    type: action', '  edges:', '  - id: y'].join('\r\n');
    expect(findNodeIdLine(yaml, 'x')).toBe(3);
    expect(findNodeIdLine(yaml, 'y')).toBeNull();
  });

  it('returns null for empty input or a document without spec.nodes', () => {
    expect(findNodeIdLine('', 'a')).toBeNull();
    expect(findNodeIdLine('spec:\n  nodes:\n    - id: a\n', '')).toBeNull();
    expect(findNodeIdLine('nodes:\n  - id: a\n', 'a')).toBeNull();
  });
});

describe('canvas.store requestYamlReveal', () => {
  beforeEach(() => { setActivePinia(createPinia()); });

  it('bumps seq on every request so repeat requests still notify watchers', () => {
    const store = useCanvasStore();
    store.requestYamlReveal('a');
    expect(store.yamlRevealRequest).toEqual({ nodeId: 'a', seq: 1, explicit: false });
    store.requestYamlReveal('a', true);
    expect(store.yamlRevealRequest).toEqual({ nodeId: 'a', seq: 2, explicit: true });
    store.reset();
    expect(store.yamlRevealRequest).toBeNull();
  });
});
