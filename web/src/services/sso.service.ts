import { http, unwrap, BASE_URL } from './api.service';
import type { ApiEnvelope } from '@/types';

export interface SsoStatus {
  enabled: boolean;
  provider: string | null;
  /** The organisation requires SSO: members can't use a password (owners still can). */
  required?: boolean;
}

export type WorkspaceRoleName = 'Viewer' | 'Runner' | 'Operator' | 'Designer' | 'Admin';

export interface SsoGroupRoleMapping {
  group: string;
  /** null: every workspace. */
  workspaceId: string | null;
  role: WorkspaceRoleName;
}

export interface SsoConfig {
  orgId: string;
  provider: string;
  isActive: boolean;
  idpEntityId: string | null;
  idpSsoUrl: string | null;
  spEntityId: string | null;
  issuerUrl: string | null;
  clientId: string | null;
  scopes: string[];
  hasCertificate: boolean;
  hasClientSecret: boolean;
  requireSso: boolean;
  jitProvisioning: boolean;
  defaultRole: WorkspaceRoleName;
  groupsAttribute: string;
  groupRoleMappings: SsoGroupRoleMapping[] | null;
  /** What the IdP admin needs. */
  acsUrl: string | null;
  metadataUrl: string | null;
  oidcRedirectUri: string | null;
  /** False when the organisation's plan doesn't include SSO (Enterprise only). */
  planIncludesSso: boolean;
}

export interface ConfigureSsoRequest {
  provider: 'saml' | 'oidc';
  isActive: boolean;
  idpEntityId?: string | null;
  idpSsoUrl?: string | null;
  idpCertificate?: string | null;
  issuerUrl?: string | null;
  clientId?: string | null;
  clientSecret?: string | null;
  scopes?: string[] | null;
  requireSso?: boolean;
  jitProvisioning?: boolean;
  defaultRole?: WorkspaceRoleName;
  groupsAttribute?: string;
  groupRoleMappings?: SsoGroupRoleMapping[];
}

export interface SsoTestResult {
  ok: boolean;
  message: string;
}

export const ssoService = {
  async getStatus(orgSlug: string): Promise<SsoStatus> {
    const res = await http.get<ApiEnvelope<SsoStatus>>(`/api/v1/auth/sso/${encodeURIComponent(orgSlug)}`);
    return unwrap(res);
  },

  async getConfig(orgId: string): Promise<SsoConfig | null> {
    const res = await http.get<ApiEnvelope<SsoConfig | null>>(`/api/v1/organisations/${orgId}/sso`);
    return unwrap(res);
  },

  async configure(orgId: string, payload: ConfigureSsoRequest): Promise<SsoConfig> {
    const res = await http.put<ApiEnvelope<SsoConfig>>(`/api/v1/organisations/${orgId}/sso`, payload);
    return unwrap(res);
  },

  async test(orgId: string): Promise<SsoTestResult> {
    const res = await http.post<ApiEnvelope<SsoTestResult>>(`/api/v1/organisations/${orgId}/sso/test`, {});
    return unwrap(res);
  },

  async disable(orgId: string): Promise<void> {
    await http.delete(`/api/v1/organisations/${orgId}/sso`);
  },

  /** Absolute URL the browser navigates to so the backend redirects to the IdP. Only same-site paths return. */
  initiateUrl(orgSlug: string, provider: string, returnTo?: string): string {
    const slug = encodeURIComponent(orgSlug);
    const path = provider === 'saml' ? `/api/v1/saml/${slug}/login` : `/api/v1/oidc/${slug}/initiate`;
    const back = returnTo && returnTo.startsWith('/') && !returnTo.startsWith('//') ? `?returnUrl=${encodeURIComponent(returnTo)}` : '';
    return `${BASE_URL}${path}${back}`;
  },
};
