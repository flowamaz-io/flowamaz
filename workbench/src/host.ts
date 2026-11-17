import { inject, type App, type InjectionKey } from 'vue';
import type { WorkbenchApi } from './api';

/**
 * What the host application gives the workbench screens: the API they use and, optionally, its help system.
 * Install it once with {@link installWorkbench} (or provide {@link WORKBENCH_HOST} on a subtree).
 */
export interface WorkbenchHost {
  /** The API for the current workspace. Optional for screens that only use the basic components. */
  api?: WorkbenchApi;
  /** A help article for an error, by HTTP status or API error code; null when there is none. */
  articleForError?: (status?: number, code?: string) => string | null;
  /** Opens a help article. Without it, no help links are shown. */
  openHelp?: (article: string) => void;
  /**
   * Opens one of the host's own pages that the screens point to (where credentials or webhooks are managed). Without
   * it, the screens name the page in words instead of linking to it.
   */
  openPage?: (page: WorkbenchPage) => void;
}

/** Host pages the workbench screens can link to. */
export type WorkbenchPage = 'credentials' | 'webhooks';

export const WORKBENCH_HOST: InjectionKey<WorkbenchHost> = Symbol('flowamaz-workbench-host');

export function installWorkbench(app: App, host: WorkbenchHost): void {
  app.provide(WORKBENCH_HOST, host);
}

/** The host, or an empty one (no API, no help) when none is installed. */
export function useWorkbenchHost(): WorkbenchHost {
  return inject(WORKBENCH_HOST, {});
}

/** The host's API; throws an actionable error when the host didn't provide one. */
export function useWorkbenchApi(): WorkbenchApi {
  const api = useWorkbenchHost().api;
  if (!api) {
    throw new Error(
      'This workbench screen needs an API. Install one with installWorkbench(app, { api }), for example createEngineApiClient(...).',
    );
  }
  return api;
}
