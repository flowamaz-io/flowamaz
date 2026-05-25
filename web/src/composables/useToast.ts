import { useUiStore } from '@/stores/ui.store';

/** Thin convenience wrapper over the UI store's toast queue. */
export function useToast() {
  const ui = useUiStore();
  return {
    success: (message: string) => ui.addToast('success', message),
    error: (message: string) => ui.addToast('error', message),
    info: (message: string) => ui.addToast('info', message),
    warning: (message: string) => ui.addToast('warning', message),
  };
}
