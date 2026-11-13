import { describe, expect, it } from 'vitest';
import type { ConversationExtraction } from '@/services/creation.service';
import {
  cloneExtraction,
  conversationProblems,
  emptyApprover,
  emptyDecision,
  move,
  parseList,
  toSubmittable,
} from '@/utils/conversationReview.util';

const extraction = (): ConversationExtraction => ({
  workflowName: ' Purchase approval ',
  summary: 'Approves purchases',
  trigger: 'A request is submitted',
  participants: [{ name: 'Anita', messageCount: 2 }],
  steps: [
    { title: ' Submit request ', owner: ' Anita ', system: '', saidBy: 'Anita' },
    { title: 'Approve', owner: 'Rajan', system: null, saidBy: null },
  ],
  decisions: [{ condition: 'amount above $5,000', outcome: 'CFO approves', saidBy: 'Rajan' }],
  approvers: [{ who: 'Rajan', when: 'up to $5,000' }],
  agreements: ['Rajan approves up to $5,000'],
  systems: ['SAP'],
  openQuestions: [],
  confidence: 0.9,
});

describe('conversation review', () => {
  it('cloneExtraction is a deep copy', () => {
    const original = extraction();
    const copy = cloneExtraction(original);
    copy.steps[0].title = 'changed';
    copy.systems.push('Jira');
    expect(original.steps[0].title).toBe(' Submit request ');
    expect(original.systems).toEqual(['SAP']);
  });

  it('toSubmittable trims, nulls empty optional fields and drops rows left empty', () => {
    const e = extraction();
    e.decisions.push(emptyDecision());
    e.approvers.push(emptyApprover());
    const s = toSubmittable(e);
    expect(s.workflowName).toBe('Purchase approval');
    expect(s.steps[0]).toEqual({ title: 'Submit request', owner: 'Anita', system: null, saidBy: 'Anita' });
    expect(s.decisions).toHaveLength(1);
    expect(s.approvers).toHaveLength(1);
  });

  it('a complete extraction has no problems', () => {
    expect(conversationProblems(extraction())).toEqual([]);
  });

  it('blocks generation with no steps, blank steps or half-filled conditions', () => {
    const none = { ...extraction(), steps: [] };
    expect(conversationProblems(none)[0]).toMatch(/Every step is removed/);

    const blank = extraction();
    blank.steps[1].title = '   ';
    expect(conversationProblems(blank)).toContain('1 step(s) have no text. Describe each one or remove it.');

    const half = extraction();
    half.decisions.push({ condition: 'urgent', outcome: '', saidBy: null });
    expect(conversationProblems(half)[0]).toMatch(/missing the "if" or the "then"/);
  });

  it('move reorders and ignores moves past either end', () => {
    const items = ['a', 'b', 'c'];
    move(items, 2, -1);
    expect(items).toEqual(['a', 'c', 'b']);
    move(items, 0, -1);
    move(items, 2, 1);
    expect(items).toEqual(['a', 'c', 'b']);
  });

  it('parseList splits on commas and de-duplicates', () => {
    expect(parseList('SAP, Slack , ,sap,Jira')).toEqual(['SAP', 'Slack', 'Jira']);
  });
});
