import { http, unwrap } from './api.service';
import type { ApiEnvelope } from '@/types';

export interface ConnectorDefinition {
  id: string;
  connectorId: string;
  publisherId: string;
  displayName: string;
  version: string;
  category: string;
  tags: string[];
  manifestJson: string;
  tier: 'Official' | 'Community' | 'Verified' | 'Marketplace';
  isEnabled: boolean;
  isInstalled: boolean;
}

export interface ConnectorHealthStatus {
  connectorId: string;
  credentialName: string;
  status: 'healthy' | 'expired' | 'rate_limited' | 'error';
  lastUsedAt: string | null;
  errorMessage: string | null;
  expiresAt: string | null;
  callsLastHour: number;
}

export interface OAuthInitiateResult {
  authorizationUrl: string;
  state: string;
}

const base = (workspaceId: string): string =>
  `/api/v1/workspaces/${workspaceId}/connectors`;

export const connectorService = {
  async getAllConnectors(workspaceId: string): Promise<ConnectorDefinition[]> {
    const res = await http.get<ApiEnvelope<ConnectorDefinition[]>>(base(workspaceId));
    return unwrap(res);
  },

  async getConnectorById(workspaceId: string, connectorId: string): Promise<ConnectorDefinition> {
    const res = await http.get<ApiEnvelope<ConnectorDefinition>>(
      `${base(workspaceId)}/${connectorId}`,
    );
    return unwrap(res);
  },

  async installConnector(
    workspaceId: string,
    connectorId: string,
    credentialId?: string,
  ): Promise<void> {
    await http.post(`${base(workspaceId)}/${connectorId}/install`, { credentialId });
  },

  async uninstallConnector(workspaceId: string, connectorId: string): Promise<void> {
    await http.delete(`${base(workspaceId)}/${connectorId}/uninstall`);
  },

  async getInstalledConnectors(workspaceId: string): Promise<ConnectorDefinition[]> {
    const res = await http.get<ApiEnvelope<ConnectorDefinition[]>>(
      `${base(workspaceId)}/installed`,
    );
    return unwrap(res);
  },

  async getConnectorHealth(workspaceId: string): Promise<ConnectorHealthStatus[]> {
    const res = await http.get<ApiEnvelope<ConnectorHealthStatus[]>>(
      `${base(workspaceId)}/health`,
    );
    return unwrap(res);
  },

  async initiateOAuth(workspaceId: string, connectorId: string): Promise<OAuthInitiateResult> {
    const res = await http.post<ApiEnvelope<OAuthInitiateResult>>(
      `${base(workspaceId)}/${connectorId}/oauth/initiate`,
      {},
    );
    return unwrap(res);
  },
};
