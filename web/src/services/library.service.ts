import { http, unwrap } from './api.service';
import type { ApiEnvelope } from '@/types';

// The Library as this workspace sees it (FUNCTIONAL.md §23): per-workspace install state, pinned majors,
// upgrades and deprecation notices. Mirrors Flowamaz.Core.Models.LibraryModels.

export interface ChangelogEntry {
  version: string;
  notes: string;
  releasedOn: string | null;
}

export interface LibraryConnectorItem {
  id: string;
  publisher: string;
  name: string;
  version: string;
  category: string;
  tags: string[];
  isOfficial: boolean;
  /** The pin a new install gets, e.g. "flowamaz-io/slack@2". */
  pin: string;
  installed: boolean;
  installedVersion: string | null;
  installedPin: string | null;
  deprecatedAt: string | null;
  removalAt: string | null;
  deprecationNote: string | null;
}

export interface LibraryConnectorDetail {
  connector: LibraryConnectorItem;
  operations: string[];
  changelog: ChangelogEntry[];
  installCount: number;
  averageRating: number;
}

export interface LibraryOutdatedItem {
  id: string;
  publisher: string;
  name: string;
  installedVersion: string;
  installedPin: string;
  latestVersion: string | null;
  changes: ChangelogEntry[];
  deprecatedAt: string | null;
  removalAt: string | null;
  deprecationNote: string | null;
}

export interface LibraryUpdateResult {
  id: string;
  fromVersion: string;
  toVersion: string;
  pin: string;
}

const base = (workspaceId: string): string => `/api/v1/workspaces/${workspaceId}/library`;

export const libraryService = {
  async listConnectors(workspaceId: string): Promise<LibraryConnectorItem[]> {
    return unwrap(await http.get<ApiEnvelope<LibraryConnectorItem[]>>(`${base(workspaceId)}/connectors`));
  },

  async getConnector(workspaceId: string, connectorId: string): Promise<LibraryConnectorDetail> {
    return unwrap(await http.get<ApiEnvelope<LibraryConnectorDetail>>(`${base(workspaceId)}/connectors/${connectorId}`));
  },

  async outdated(workspaceId: string): Promise<LibraryOutdatedItem[]> {
    return unwrap(await http.get<ApiEnvelope<LibraryOutdatedItem[]>>(`${base(workspaceId)}/connectors/outdated`));
  },

  /** Admin only: move an installed connector to its latest major. */
  async upgrade(workspaceId: string, connectorId: string): Promise<LibraryUpdateResult> {
    return unwrap(await http.post<ApiEnvelope<LibraryUpdateResult>>(`${base(workspaceId)}/connectors/update`, { id: connectorId }));
  },
};

/** "2027-02-11T…" → "11 Feb 2027" for notices. */
export function noticeDate(iso: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? '' : d.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' });
}
