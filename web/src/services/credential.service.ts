import { http, unwrap } from './api.service';
import type { ApiEnvelope } from '@/types';
import type { ConnectorDefinition } from './connector.service';

/** A credential alias. Values are write-only and never returned by the API. */
export interface CredentialAlias {
  id: string;
  name: string;
  authType: string;
  expiresAt: string | null;
  isActive: boolean;
  connectorId: string | null;
  lastUsedAt: string | null;
}

export type ManualAuthType = 'ApiKey' | 'BearerToken' | 'BasicAuth' | 'ConnectionString' | 'HmacSecret';

export interface CreateCredentialRequest {
  connectorId: string;
  name: string;
  authType: ManualAuthType;
  value: string;
}

/** Operation entry in a connector manifest (published by the backend catalog). */
export interface ManifestOperation {
  id: string;
  name?: string;
  description?: string;
  requires_credential?: boolean;
  internal?: boolean;
  input_schema?: {
    required?: string[];
    properties?: Record<string, { description?: string; type?: string }>;
  };
}

export interface ConnectorManifest {
  auth?: { type?: string; credential_types?: string[] };
  operations?: ManifestOperation[];
  triggers?: string[];
}

export function parseManifest(connector: ConnectorDefinition | undefined): ConnectorManifest {
  if (!connector) return {};
  try {
    return JSON.parse(connector.manifestJson) as ConnectorManifest;
  } catch {
    return {};
  }
}

/** OAuth connectors are connected through the provider sign-in, not a pasted value. */
export function isOAuthConnector(manifest: ConnectorManifest): boolean {
  const type = manifest.auth?.type?.toLowerCase() ?? '';
  return type.startsWith('oauth') || (manifest.auth?.credential_types ?? []).includes('OAuth2AuthCode');
}

/** Credential types the user can paste for this connector (OAuth excluded). */
export function manualCredentialTypes(manifest: ConnectorManifest): ManualAuthType[] {
  const manual: ManualAuthType[] = ['ApiKey', 'BearerToken', 'BasicAuth', 'ConnectionString', 'HmacSecret'];
  return (manifest.auth?.credential_types ?? []).filter((t): t is ManualAuthType =>
    (manual as string[]).includes(t),
  );
}

export const AUTH_TYPE_LABELS: Record<string, string> = {
  ApiKey: 'API key',
  BearerToken: 'Bearer token / personal access token',
  BasicAuth: 'Username and password',
  ConnectionString: 'Connection string',
  HmacSecret: 'Signing secret (HMAC)',
  OAuth2AuthCode: 'OAuth sign-in',
};

const base = (workspaceId: string) => `/api/v1/workspaces/${workspaceId}/credentials`;

export const credentialService = {
  async list(workspaceId: string, connectorId?: string): Promise<CredentialAlias[]> {
    const res = await http.get<ApiEnvelope<CredentialAlias[]>>(base(workspaceId), {
      params: connectorId ? { connectorId } : undefined,
    });
    return unwrap(res);
  },

  async create(workspaceId: string, request: CreateCredentialRequest): Promise<CredentialAlias> {
    const res = await http.post<ApiEnvelope<CredentialAlias>>(base(workspaceId), request);
    return unwrap(res);
  },

  async revoke(workspaceId: string, credentialId: string): Promise<void> {
    await http.delete(`${base(workspaceId)}/${credentialId}`);
  },
};
