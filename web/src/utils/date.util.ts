import dayjs from 'dayjs';
import relativeTime from 'dayjs/plugin/relativeTime';

dayjs.extend(relativeTime);

/** Human-readable date, e.g. "25 May 2026". */
export function formatDate(value: string | null | undefined): string {
  if (!value) return '—';
  return dayjs(value).format('D MMM YYYY');
}

/** Relative time, e.g. "3 days ago". */
export function fromNow(value: string | null | undefined): string {
  if (!value) return 'Never';
  return dayjs(value).fromNow();
}
