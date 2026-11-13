import { http, unwrap } from './api.service';
import type { ApiEnvelope, GateFormField } from '@/types';

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

export interface LibraryFormItem {
  id: string;
  slug: string;
  name: string;
  description: string;
  category: string;
  version: string;
  publisher: string | null;
  isOfficial: boolean;
  /** Saved by this workspace (can be deleted here); otherwise a Library form. */
  isWorkspaceForm: boolean;
  fields: GateFormField[];
  tags: string[];
  deprecatedAt: string | null;
  deprecationNote: string | null;
}

export interface LibraryPackEntry {
  kind: 'connector' | 'template' | 'form';
  id: string;
  name: string;
  available: boolean;
  installed: boolean;
}

export interface LibraryPackItem {
  id: string;
  slug: string;
  name: string;
  description: string;
  category: string;
  version: string;
  publisher: string | null;
  isOfficial: boolean;
  items: LibraryPackEntry[];
  tags: string[];
  deprecatedAt: string | null;
  deprecationNote: string | null;
}

export interface LibraryPackItemResult {
  kind: 'connector' | 'template' | 'form';
  id: string;
  name: string;
  status: 'installed' | 'already_installed' | 'unavailable' | 'failed';
  message: string;
  workflowId: string | null;
}

export interface LibraryPackInstallResult {
  pack: string;
  installed: number;
  alreadyInstalled: number;
  failed: number;
  items: LibraryPackItemResult[];
}

export interface SaveFormRequest {
  name: string;
  description?: string;
  category?: string;
  fields: GateFormField[];
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

  async listForms(workspaceId: string, query?: string): Promise<LibraryFormItem[]> {
    const params = query ? { q: query } : undefined;
    return unwrap(await http.get<ApiEnvelope<LibraryFormItem[]>>(`${base(workspaceId)}/forms`, { params }));
  },

  /** Designer: keeps a gate's fields as one of this workspace's forms. */
  async saveForm(workspaceId: string, request: SaveFormRequest): Promise<LibraryFormItem> {
    return unwrap(await http.post<ApiEnvelope<LibraryFormItem>>(`${base(workspaceId)}/forms`, request));
  },

  async deleteForm(workspaceId: string, formId: string): Promise<void> {
    await http.delete(`${base(workspaceId)}/forms/${formId}`);
  },

  async listPacks(workspaceId: string): Promise<LibraryPackItem[]> {
    return unwrap(await http.get<ApiEnvelope<LibraryPackItem[]>>(`${base(workspaceId)}/packs`));
  },

  /** Designer: installs the pack's connectors and templates; the result says what happened to each. */
  async installPack(workspaceId: string, slug: string): Promise<LibraryPackInstallResult> {
    return unwrap(await http.post<ApiEnvelope<LibraryPackInstallResult>>(`${base(workspaceId)}/packs/install`, { id: slug }));
  },
};

/** "2027-02-11T…" → "11 Feb 2027" for notices. */
export function noticeDate(iso: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? '' : d.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' });
}
