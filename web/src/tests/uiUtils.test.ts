import { describe, it, expect } from 'vitest';
import { humanizeId, stripMarkdown } from '@/utils/string.util';
import { placeTourCard, TOUR_GAP } from '@/utils/tour.util';

describe('stripMarkdown', () => {
  it('removes headings, emphasis, inline code and link URLs', () => {
    expect(stripMarkdown('## Create a key\n\nUse **API keys** with `curl` — see [docs](https://x.io).')).toBe(
      'Create a key Use API keys with curl — see docs.',
    );
  });

  it('drops code fences, list markers, quotes and table pipes', () => {
    const md = '- one\n- two\n\n> note\n\n```yaml\nkey: value\n```\n\n| A | B |\n|---|---|\n| 1 | 2 |';
    expect(stripMarkdown(md)).toBe('one two note A B 1 2');
  });

  it('keeps snake_case words intact', () => {
    expect(stripMarkdown('Set timeout_hours to 24')).toBe('Set timeout_hours to 24');
  });
});

describe('humanizeId', () => {
  it('turns machine ids into sentence case', () => {
    expect(humanizeId('manager-approval')).toBe('Manager approval');
    expect(humanizeId('send_email')).toBe('Send email');
    expect(humanizeId('riskReview')).toBe('Risk review');
    expect(humanizeId('')).toBe('');
  });
});

describe('placeTourCard', () => {
  const viewport = { width: 1280, height: 800 };
  const card = { width: 352, height: 240 };
  const inside = (pos: { top: number; left: number }): boolean =>
    pos.top >= TOUR_GAP &&
    pos.left >= TOUR_GAP &&
    pos.top + card.height <= viewport.height - TOUR_GAP + 0.001 &&
    pos.left + card.width <= viewport.width - TOUR_GAP + 0.001;

  it('centres when there is no target', () => {
    expect(placeTourCard(null, card, viewport)).toBeNull();
  });

  it('places the card below a small target near the top', () => {
    const pos = placeTourCard({ top: 80, left: 300, width: 400, height: 120 }, card, viewport);
    expect(pos).toEqual({ top: 80 + 120 + TOUR_GAP, left: 300 });
  });

  it('places the card beside a full-height sidebar, inside the viewport (O1)', () => {
    const pos = placeTourCard({ top: 0, left: 0, width: 256, height: 800 }, card, viewport);
    expect(pos).not.toBeNull();
    expect(pos!.left).toBe(256 + TOUR_GAP);
    expect(inside(pos!)).toBe(true);
  });

  it('clamps horizontally for a target at the right edge', () => {
    const pos = placeTourCard({ top: 60, left: 1200, width: 60, height: 40 }, card, viewport);
    expect(inside(pos!)).toBe(true);
  });

  it('falls back to centred when nothing fits', () => {
    expect(placeTourCard({ top: 0, left: 0, width: 360, height: 640 }, card, { width: 375, height: 640 })).toBeNull();
  });
});
