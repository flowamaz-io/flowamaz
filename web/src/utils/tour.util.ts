export interface Box {
  top: number;
  left: number;
  width: number;
  height: number;
}

export interface Size {
  width: number;
  height: number;
}

/** Gap between the spotlight and the card, and the minimum margin to the viewport edge. */
export const TOUR_GAP = 16;

const clamp = (value: number, min: number, max: number): number => Math.min(Math.max(value, min), Math.max(min, max));

/**
 * Picks a top/left for the product-tour card next to the spotlight `target`, trying
 * below, above, right, then left, and clamping the result so the whole card stays inside
 * the viewport. Returns null (centre the card) when there is no target or no side fits —
 * e.g. a full-height sidebar on a narrow screen.
 */
export function placeTourCard(target: Box | null, card: Size, viewport: Size): { top: number; left: number } | null {
  if (!target) return null;
  const maxLeft = viewport.width - card.width - TOUR_GAP;
  const maxTop = viewport.height - card.height - TOUR_GAP;
  const bottom = target.top + target.height;
  const right = target.left + target.width;

  // Below
  if (bottom + TOUR_GAP + card.height + TOUR_GAP <= viewport.height) {
    return { top: bottom + TOUR_GAP, left: clamp(target.left, TOUR_GAP, maxLeft) };
  }
  // Above
  if (target.top - TOUR_GAP - card.height >= TOUR_GAP) {
    return { top: target.top - TOUR_GAP - card.height, left: clamp(target.left, TOUR_GAP, maxLeft) };
  }
  // Right
  if (right + TOUR_GAP + card.width + TOUR_GAP <= viewport.width) {
    return { top: clamp(target.top, TOUR_GAP, maxTop), left: right + TOUR_GAP };
  }
  // Left
  if (target.left - TOUR_GAP - card.width >= TOUR_GAP) {
    return { top: clamp(target.top, TOUR_GAP, maxTop), left: target.left - TOUR_GAP - card.width };
  }
  return null;
}
