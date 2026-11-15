import type { ElementDefinition } from 'cytoscape';

export type NodeType =
  | 'trigger'
  | 'action'
  | 'ai'
  | 'human-gate'
  | 'router'
  | 'end'
  | 'foreach'
  | 'parallel'
  | 'try-catch'
  | 'wait'
  | 'sub-workflow'
  | 'annotation';

export interface NodeVisual {
  color: string;
  icon: string;
  shape: string;
}

export const NODE_VISUALS: Record<NodeType, NodeVisual> = {
  trigger:      { color: '#534AB7', icon: 'Zap',          shape: 'roundrectangle' },
  action:       { color: '#1D9E75', icon: 'Play',          shape: 'roundrectangle' },
  ai:           { color: '#378ADD', icon: 'Brain',         shape: 'roundrectangle' },
  'human-gate': { color: '#EF9F27', icon: 'UserCheck',     shape: 'diamond' },
  router:       { color: '#E24B4A', icon: 'GitBranch',     shape: 'diamond' },
  end:          { color: '#888780', icon: 'CircleStop',     shape: 'ellipse' },
  foreach:      { color: '#1D9E75', icon: 'Repeat',        shape: 'roundrectangle' },
  parallel:     { color: '#378ADD', icon: 'Split',         shape: 'roundrectangle' },
  'try-catch':  { color: '#E24B4A', icon: 'ShieldAlert',   shape: 'roundrectangle' },
  wait:         { color: '#888780', icon: 'Clock',         shape: 'roundrectangle' },
  'sub-workflow': { color: '#534AB7', icon: 'Workflow',    shape: 'roundrectangle' },
  annotation:   { color: '#FBBF24', icon: 'StickyNote',    shape: 'roundrectangle' },
};

export interface CanvasNode {
  id: string;
  type: NodeType;
  label: string;
  config: Record<string, unknown>;
  retry?: RetryPolicy;
  timeout?: TimeoutPolicy;
  compensate?: CompensationBlock;
  content?: string;
  position?: { x: number; y: number };
}

export interface CanvasEdge {
  id: string;
  from: string;
  to: string;
  via?: string;
  condition?: string;
}

export interface WorkflowGroup {
  id: string;
  label: string;
  nodeIds: string[];
  color: string;
}

export interface RetryPolicy {
  maxAttempts: number;
  backoffSeconds: number;
  backoffMultiplier: number;
}

export interface TimeoutPolicy {
  seconds: number;
  onTimeout?: string;
}

export interface CompensationBlock {
  strategy: 'backward' | 'forward' | 'pivot';
  nodeId?: string;
}

export interface CanvasState {
  nodes: CanvasNode[];
  edges: CanvasEdge[];
  groups: WorkflowGroup[];
  selectedNodeId: string | null;
  history: string[];
  historyIndex: number;
  isDirty: boolean;
  isSyncing: boolean;
}

export type CytoscapeNodeDefinition = ElementDefinition;
export type CytoscapeEdgeDefinition = ElementDefinition;
