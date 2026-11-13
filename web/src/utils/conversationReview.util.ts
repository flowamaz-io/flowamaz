import type {
  ConversationApprover,
  ConversationDecision,
  ConversationExtraction,
  ConversationStep,
} from '@/services/creation.service';

/** Limits the server enforces on a reviewed extraction (ConversationExtractionValidator). */
export const MAX_STEPS = 23;
const MAX_TEXT = 500;

/** A deep copy the review form can edit freely. */
export function cloneExtraction(e: ConversationExtraction): ConversationExtraction {
  return {
    ...e,
    participants: e.participants.map((p) => ({ ...p })),
    steps: e.steps.map((s) => ({ ...s })),
    decisions: e.decisions.map((d) => ({ ...d })),
    approvers: e.approvers.map((a) => ({ ...a })),
    agreements: [...e.agreements],
    systems: [...e.systems],
    openQuestions: [...e.openQuestions],
  };
}

export function emptyStep(): ConversationStep {
  return { title: '', owner: null, system: null, saidBy: null };
}

export function emptyDecision(): ConversationDecision {
  return { condition: '', outcome: '', saidBy: null };
}

export function emptyApprover(): ConversationApprover {
  return { who: '', when: null };
}

/** Moves item `index` by `delta` (−1 up, +1 down); out-of-range moves are ignored. */
export function move<T>(items: T[], index: number, delta: number): void {
  const target = index + delta;
  if (index < 0 || index >= items.length || target < 0 || target >= items.length) return;
  const [item] = items.splice(index, 1);
  items.splice(target, 0, item);
}

/** "SAP, Slack , ,Jira" → ["SAP", "Slack", "Jira"] (case-insensitive de-duplication). */
export function parseList(text: string): string[] {
  const seen = new Set<string>();
  const out: string[] = [];
  for (const part of text.split(',')) {
    const v = part.trim();
    if (v && !seen.has(v.toLowerCase())) {
      seen.add(v.toLowerCase());
      out.push(v);
    }
  }
  return out;
}

const opt = (v: string | null): string | null => {
  const t = (v ?? '').trim();
  return t.length > 0 ? t : null;
};

/**
 * What is sent to POST /create: text trimmed, empty optional fields as null, rows the user left completely empty
 * (a decision or approver added and never filled in) dropped.
 */
export function toSubmittable(e: ConversationExtraction): ConversationExtraction {
  return {
    ...cloneExtraction(e),
    workflowName: e.workflowName.trim(),
    summary: e.summary.trim(),
    trigger: e.trigger.trim(),
    steps: e.steps.map((s) => ({ title: s.title.trim(), owner: opt(s.owner), system: opt(s.system), saidBy: s.saidBy })),
    decisions: e.decisions
      .filter((d) => d.condition.trim() || d.outcome.trim())
      .map((d) => ({ condition: d.condition.trim(), outcome: d.outcome.trim(), saidBy: d.saidBy })),
    approvers: e.approvers.filter((a) => a.who.trim()).map((a) => ({ who: a.who.trim(), when: opt(a.when) })),
    agreements: e.agreements.map((a) => a.trim()).filter(Boolean),
  };
}

/** Problems that block generation, phrased as next steps. Empty = ready. */
export function conversationProblems(e: ConversationExtraction): string[] {
  const s = toSubmittable(e);
  const problems: string[] = [];
  if (s.steps.length === 0) {
    problems.push('Every step is removed. Add at least one step, or describe the process in plain English instead.');
  }
  if (s.steps.length === 1) {
    problems.push('A workflow needs at least two steps. Add the step before or after this one (who does what next).');
  }
  if (s.steps.length > MAX_STEPS) {
    problems.push(`There are more than ${MAX_STEPS} steps. Split the process into smaller workflows.`);
  }
  const blank = s.steps.filter((x) => x.title.length === 0).length;
  if (blank > 0) problems.push(`${blank} step(s) have no text. Describe each one or remove it.`);
  const halfDecisions = s.decisions.filter((d) => !d.condition || !d.outcome).length;
  if (halfDecisions > 0) {
    problems.push(`${halfDecisions} condition(s) are missing the "if" or the "then". Fill in both, or remove the condition.`);
  }
  const tooLong = [
    ...s.steps.flatMap((x) => [x.title, x.owner ?? '', x.system ?? '']),
    ...s.decisions.flatMap((d) => [d.condition, d.outcome]),
    ...s.approvers.flatMap((a) => [a.who, a.when ?? '']),
  ].some((t) => t.length > MAX_TEXT);
  if (tooLong) problems.push(`Some text is longer than ${MAX_TEXT} characters. Shorten it to keep each entry to one point.`);
  return problems;
}
