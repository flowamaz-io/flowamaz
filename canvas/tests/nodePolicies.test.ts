import { describe, expect, it } from 'vitest';
import { parseWorkflowGraph, readNodePolicies, writeNodePolicies } from '../src/workflowGraph';

describe('retry / timeout / compensate blocks', () => {
  it('reads the documented snake_case form', () => {
    expect(readNodePolicies({
      retry: { max_attempts: 3, backoff_seconds: 5, backoff_multiplier: 2 },
      timeout: { seconds: 30, on_timeout: 'handler' },
      compensate: { strategy: 'backward', node_id: 'undo' },
    })).toEqual({
      retry: { maxAttempts: 3, backoffSeconds: 5, backoffMultiplier: 2 },
      timeout: { seconds: 30, onTimeout: 'handler' },
      compensate: { strategy: 'backward', nodeId: 'undo' },
    });
  });

  it('reads the camelCase form older versions wrote', () => {
    expect(readNodePolicies({
      retry: { maxAttempts: 4, backoffSeconds: 1 },
      timeout: { timeoutSeconds: 20, onTimeoutNodeId: 'h' },
      compensation: { strategy: 'forward', compensateNodeId: 'u' },
    })).toEqual({
      retry: { maxAttempts: 4, backoffSeconds: 1, backoffMultiplier: 1 },
      timeout: { seconds: 20, onTimeout: 'h' },
      compensate: { strategy: 'forward', nodeId: 'u' },
    });
  });

  it('writes only the documented form, dropping older spellings', () => {
    const node: Record<string, unknown> = { id: 'a', compensation: { strategy: 'forward' } };
    writeNodePolicies(node, {
      retry: { maxAttempts: 3, backoffSeconds: 5, backoffMultiplier: 2 },
      timeout: { seconds: 45 },
      compensate: { strategy: 'backward', nodeId: 'undo' },
    });
    expect(node).toEqual({
      id: 'a',
      retry: { max_attempts: 3, backoff_seconds: 5, backoff_multiplier: 2 },
      timeout: { seconds: 45 },
      compensate: { strategy: 'backward', node_id: 'undo' },
    });
  });

  it('null removes a block; undefined leaves it', () => {
    const node: Record<string, unknown> = { id: 'a', retry: { max_attempts: 2 }, timeout: { seconds: 5 } };
    writeNodePolicies(node, { retry: null });
    expect(node).toEqual({ id: 'a', timeout: { seconds: 5 } });
  });

  it('parseWorkflowGraph puts them on the canvas node', () => {
    const graph = parseWorkflowGraph(`
nodes:
  - { id: start, type: trigger }
  - id: call
    type: action
    retry: { max_attempts: 3, backoff_seconds: 5, backoff_multiplier: 2 }
edges:
  - { from: start, to: call }
`);
    expect(graph?.nodes.find((n) => n.id === 'call')?.retry).toEqual({ maxAttempts: 3, backoffSeconds: 5, backoffMultiplier: 2 });
  });
});
