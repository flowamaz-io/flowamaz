import { describe, expect, it } from 'vitest';
import { runDuration } from '../src/runs';

const start = '2026-11-16T00:00:00.000Z';

describe('runDuration', () => {
  it('measures a successful run to completedAt', () => {
    expect(runDuration({ status: 'Completed', startedAt: start, completedAt: '2026-11-16T00:00:02.000Z' })).toBe('2.0s');
  });

  it('measures a failed run to failedAt instead of counting up', () => {
    expect(
      runDuration({ status: 'Failed', startedAt: start, completedAt: null, failedAt: '2026-11-16T00:00:04.000Z' }),
    ).toBe('4.0s');
  });

  it('shows a dash for an ended run with no end time (cancelled)', () => {
    expect(runDuration({ status: 'Cancelled', startedAt: start, completedAt: null })).toBe('—');
  });

  it('counts up while the run is still going', () => {
    expect(runDuration({ status: 'Running', startedAt: start, completedAt: null })).not.toBe('—');
  });
});
