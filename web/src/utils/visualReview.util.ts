import type {
  ConfirmedVisualGraph,
  DetectedEdge,
  VisualPreviewResult,
} from '@/services/creation.service';

/** One detected step as the user is reviewing it. */
export interface ReviewItem {
  id: string;
  label: string;
  originalLabel: string;
  type: string;
  confidence: number;
  reviewNeeded: boolean;
  reason: string | null;
  suggestedConnectors: string[];
  keep: boolean;
}

export interface ReviewState {
  items: ReviewItem[];
  edges: DetectedEdge[];
}

/** Builds the editable review state from the preview. Every node starts as kept. */
export function initReview(preview: VisualPreviewResult): ReviewState {
  return {
    items: preview.nodes.map((n) => ({
      id: n.id,
      label: n.label,
      originalLabel: n.label,
      type: n.type,
      confidence: n.confidence,
      reviewNeeded: n.reviewNeeded,
      reason: n.reason,
      suggestedConnectors: [...n.suggestedConnectors],
      keep: true,
    })),
    edges: preview.edges.map((e) => ({ ...e })),
  };
}

/** Low-confidence items first (lowest confidence first), then the rest in detection order. */
export function itemsNeedingReview(items: ReviewItem[]): ReviewItem[] {
  return items.filter((i) => i.reviewNeeded).sort((a, b) => a.confidence - b.confidence);
}

/**
 * Problems that block creation, phrased as next steps. Empty array = ready to create.
 */
export function reviewProblems(items: ReviewItem[]): string[] {
  const problems: string[] = [];
  const kept = items.filter((i) => i.keep);
  if (kept.length === 0) {
    problems.push('Every step is removed. Keep at least one step, or start from the canvas instead.');
  }
  const unnamed = kept.filter((i) => i.label.trim().length === 0);
  if (unnamed.length > 0) {
    problems.push(`${unnamed.length} kept step(s) have no name. Give each one a name or remove it.`);
  }
  return problems;
}

/**
 * The graph sent to POST /workflows/create (method visual). Removed steps are left out; their edges are
 * sent unchanged and the backend reconnects predecessors to successors.
 */
export function toConfirmedGraph(state: ReviewState): ConfirmedVisualGraph {
  return {
    nodes: state.items
      .filter((i) => i.keep)
      .map((i) => ({
        id: i.id,
        label: i.label.trim(),
        type: i.type,
        suggestedConnectors: i.suggestedConnectors,
      })),
    edges: state.edges.map((e) => ({ from: e.from, to: e.to, label: e.label ?? null })),
  };
}

/** "72%" — confidence as a whole percentage for display. */
export function formatConfidence(confidence: number): string {
  return `${Math.round(Math.max(0, Math.min(1, confidence)) * 100)}%`;
}
