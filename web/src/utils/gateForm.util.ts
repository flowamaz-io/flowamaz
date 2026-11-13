import type { GateFormField, GateFormFieldType, GateFormValue } from '@/types';

// Human-gate forms (FUNCTIONAL.md §23.2). The server checks again (GateForms.Check); these mirror its rules so
// the approver sees problems before sending, phrased the same way.

export const FIELD_TYPES: { value: GateFormFieldType; label: string }[] = [
  { value: 'text', label: 'Short text' },
  { value: 'textarea', label: 'Long text' },
  { value: 'number', label: 'Number' },
  { value: 'date', label: 'Date' },
  { value: 'select', label: 'Choice' },
  { value: 'checkbox', label: 'Tick box' },
  { value: 'email', label: 'Email' },
];

export const MAX_FIELDS = 30;
const KEY_RE = /^[a-z][a-z0-9_]{0,63}$/;
const EMAIL_RE = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;
const DATE_RE = /^\d{4}-\d{2}-\d{2}$/;

/** "Cost centre" → "cost_centre", for a field key the user didn't type. */
export function keyFromLabel(label: string): string {
  const key = label.toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '').replace(/^[^a-z]+/, '');
  return key.slice(0, 64) || 'field';
}

/** The initial value an approver starts from. */
export function initialValues(fields: GateFormField[]): Record<string, GateFormValue> {
  const values: Record<string, GateFormValue> = {};
  for (const f of fields) values[f.key] = f.type === 'checkbox' ? false : '';
  return values;
}

/** Problems with the approver's answers, keyed by field, or an empty object. */
export function answerProblems(fields: GateFormField[], values: Record<string, GateFormValue>): Record<string, string> {
  const problems: Record<string, string> = {};
  for (const f of fields) {
    const raw = values[f.key];
    const empty = raw === undefined || raw === null || (typeof raw === 'string' && raw.trim() === '');
    if (f.type === 'checkbox') {
      if (f.required === true && raw !== true) problems[f.key] = `${f.label} must be ticked to approve.`;
      continue;
    }
    if (empty) {
      if (f.required === true) problems[f.key] = `${f.label} is required.`;
      continue;
    }
    const text = String(raw).trim();
    if (f.type === 'number') {
      const n = Number(text);
      if (!Number.isFinite(n)) problems[f.key] = `${f.label} must be a number.`;
      else if (f.min != null && n < f.min) problems[f.key] = `${f.label} must be at least ${f.min}.`;
      else if (f.max != null && n > f.max) problems[f.key] = `${f.label} must be at most ${f.max}.`;
    } else if (f.type === 'date' && !DATE_RE.test(text)) {
      problems[f.key] = `${f.label} must be a date (YYYY-MM-DD).`;
    } else if (f.type === 'email' && !EMAIL_RE.test(text)) {
      problems[f.key] = `${f.label} must be an email address.`;
    } else if (f.type === 'select' && !(f.options ?? []).includes(text)) {
      problems[f.key] = `${f.label} must be one of: ${(f.options ?? []).join(', ')}.`;
    }
  }
  return problems;
}

/** The values to send: numbers as numbers, blanks left out. */
export function answersToSend(fields: GateFormField[], values: Record<string, GateFormValue>): Record<string, GateFormValue> {
  const out: Record<string, GateFormValue> = {};
  for (const f of fields) {
    const raw = values[f.key];
    if (f.type === 'checkbox') out[f.key] = raw === true;
    else if (typeof raw === 'string' && raw.trim() === '') continue;
    else if (f.type === 'number') out[f.key] = Number(raw);
    else if (raw !== undefined) out[f.key] = typeof raw === 'string' ? raw.trim() : raw;
  }
  return out;
}

/** What's wrong with a form definition (the gate editor), as fix-it messages. */
export function definitionProblems(fields: GateFormField[]): string[] {
  const problems: string[] = [];
  if (fields.length > MAX_FIELDS) problems.push(`A form can have up to ${MAX_FIELDS} fields. Split it across two gates.`);
  const seen = new Set<string>();
  for (const f of fields) {
    const name = f.label.trim() || f.key || 'A field';
    if (!f.label.trim()) problems.push('Every field needs a label.');
    if (!KEY_RE.test(f.key)) problems.push(`${name}: the key must be lowercase letters, numbers and underscores, starting with a letter (e.g. cost_centre).`);
    else if (seen.has(f.key)) problems.push(`${name}: the key '${f.key}' is used twice. Give each field its own key.`);
    seen.add(f.key);
    if (f.type === 'select' && (f.options ?? []).length === 0) problems.push(`${name}: add at least one choice.`);
    if (f.min != null && f.max != null && f.min > f.max) problems.push(`${name}: the minimum is above the maximum.`);
  }
  return problems;
}

/** "1200" / true → how a decided gate shows the answer. */
export function displayValue(value: GateFormValue | undefined): string {
  if (value === true) return 'Yes';
  if (value === false) return 'No';
  if (value === undefined || value === '') return '—';
  return String(value);
}
