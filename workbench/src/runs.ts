import dayjs from 'dayjs';
import type { InstanceStatus } from './types';

// Helpers for showing runs (moved from the product's src/utils/workflow.util.ts, which re-exports them).

export type BadgeVariant = 'primary' | 'accent' | 'amber' | 'danger' | 'slate' | 'blue';

/** Every run status, in the order filters list them. */
export const RUN_STATUSES: readonly InstanceStatus[] = ['Pending', 'Running', 'Waiting', 'Completed', 'Failed', 'Cancelled', 'Compensating'];

/** Statuses a run never leaves. */
export const ENDED_RUN_STATUSES: readonly string[] = ['Completed', 'Failed', 'Cancelled'];

export function isRunEnded(status: string | null | undefined): boolean {
  return !!status && ENDED_RUN_STATUSES.includes(status);
}

/** A run can still be cancelled. */
export function canCancelRun(status: string | null | undefined): boolean {
  return status === 'Pending' || status === 'Running' || status === 'Waiting' || status === 'Compensating';
}

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

/**
 * How long a run took, or has taken so far. Only a successful run has completedAt; a failed run ends at
 * failedAt. A run that ended without an end time (a cancelled run) shows "—" instead of a clock that
 * keeps counting.
 */
export function runDuration(run: {
  status: string;
  startedAt: string | null;
  completedAt: string | null;
  failedAt?: string | null;
}): string {
  const end = run.completedAt ?? run.failedAt ?? null;
  if (!end && isRunEnded(run.status)) return '—';
  return formatDuration(run.startedAt, end);
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

/** "Expires in 3h 20m" for a test run; "Expired" once past; "" without an expiry. */
export function testRunExpiry(expiresAt: string | null, now: number = Date.now()): string {
  if (!expiresAt) return '';
  const diff = new Date(expiresAt).getTime() - now;
  if (diff <= 0) return 'Expired';
  const h = Math.floor(diff / 3_600_000);
  const m = Math.floor((diff % 3_600_000) / 60_000);
  return h > 0 ? `Expires in ${h}h ${m}m` : `Expires in ${m}m`;
}
