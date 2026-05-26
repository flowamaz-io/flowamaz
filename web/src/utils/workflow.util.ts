import dayjs from 'dayjs';
import type { InstanceStatus, WorkflowStatus } from '@/types';

type BadgeVariant = 'primary' | 'accent' | 'amber' | 'danger' | 'slate' | 'blue';

/** Maps an instance status to an FmBadge colour variant. */
export function instanceStatusVariant(status: InstanceStatus): BadgeVariant {
  switch (status) {
    case 'Running':
      return 'blue';
    case 'Waiting':
    case 'Compensating':
      return 'amber';
    case 'Completed':
      return 'primary';
    case 'Failed':
      return 'danger';
    default:
      return 'slate';
  }
}

/** Maps a workflow status to an FmBadge colour variant. */
export function workflowStatusVariant(status: WorkflowStatus): BadgeVariant {
  return status === 'Published' ? 'primary' : 'slate';
}

/** Health score colour band: 80-100 green, 60-79 amber, 0-59 red. */
export function healthColorClass(score: number): string {
  if (score >= 80) return 'text-teal-600';
  if (score >= 60) return 'text-amber-600';
  return 'text-danger-600';
}

/** Human duration between two ISO timestamps (or to now if end is null). */
export function formatDuration(start: string | null, end: string | null): string {
  if (!start) return '—';
  const ms = dayjs(end ?? undefined).diff(dayjs(start));
  if (ms < 1000) return `${ms}ms`;
  if (ms < 60_000) return `${(ms / 1000).toFixed(1)}s`;
  const minutes = Math.floor(ms / 60_000);
  const seconds = Math.round((ms % 60_000) / 1000);
  return `${minutes}m ${seconds}s`;
}
