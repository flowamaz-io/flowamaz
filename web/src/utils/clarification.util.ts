import type { ClarificationAnswer, ClarifyingQuestion } from '@/services/creation.service';

/**
 * Turns the answers typed by the user into the clarifications sent with generation.
 * Blank answers mean "skipped" and are left out, so skipping everything sends nothing.
 */
export function buildClarifications(
  questions: ClarifyingQuestion[],
  answers: Record<string, string>,
): ClarificationAnswer[] {
  return questions
    .map((q) => ({ topic: q.topic, question: q.question, answer: (answers[q.id] ?? '').trim() }))
    .filter((a) => a.answer.length > 0);
}
