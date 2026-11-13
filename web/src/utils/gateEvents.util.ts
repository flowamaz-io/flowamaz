// Tells the sidebar's pending-gates badge to refresh after a decision is made elsewhere (CC, PR #23).
export const GATES_CHANGED_EVENT = 'flowamaz:gates-changed';

export function notifyGatesChanged(): void {
  window.dispatchEvent(new Event(GATES_CHANGED_EVENT));
}
