import { http, unwrap } from './api.service';
import type { ApiEnvelope } from '@/types';

export interface FieldMapping {
  fieldName: string;
  suggestedExpression: string;
  confidence: number;
}

export interface AutoMapResult {
  mappings: FieldMapping[];
  unmappedFields: string[];
}

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
  installCount: number;
  averageRating: number;
  ratingCount: number;
  isOfficial: boolean;
}

export interface ConnectorReview {
  orgId: string;
  rating: number;
  review: string | null;
  createdAt: string;
}

export interface ConnectorRatingResult {
  averageRating: number;
  ratingCount: number;
}

export interface ConnectorSubmissionResult {
  submissionId: string;
  githubPrUrl: string;
  status: string;
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

  async autoMap(
    workspaceId: string,
    connectorId: string,
    operationId: string,
    samplePayload: object,
  ): Promise<AutoMapResult> {
    const res = await http.post<ApiEnvelope<AutoMapResult>>(
      `${base(workspaceId)}/${connectorId}/operations/${operationId}/auto-map`,
      { sample_payload: samplePayload },
    );
    return unwrap(res);
  },

  async getReviews(workspaceId: string, connectorId: string): Promise<ConnectorReview[]> {
    const res = await http.get<ApiEnvelope<ConnectorReview[]>>(`${base(workspaceId)}/${connectorId}/reviews`);
    return unwrap(res);
  },

  async rateConnector(
    workspaceId: string,
    connectorId: string,
    rating: number,
    review: string | null,
  ): Promise<ConnectorRatingResult> {
    const res = await http.post<ApiEnvelope<ConnectorRatingResult>>(
      `${base(workspaceId)}/${connectorId}/rate`,
      { rating, review },
    );
    return unwrap(res);
  },

  async submitConnector(
    workspaceId: string,
    connectorName: string,
    manifestYaml: string,
  ): Promise<ConnectorSubmissionResult> {
    const res = await http.post<ApiEnvelope<ConnectorSubmissionResult>>(
      `/api/v1/workspaces/${workspaceId}/library/connectors/submit`,
      { connectorName, manifestYaml },
    );
    return unwrap(res);
  },
};

/** Formats an install count like "1.2k". */
export function formatInstalls(count: number): string {
  return count >= 1000 ? `${(count / 1000).toFixed(1)}k` : String(count);
}
