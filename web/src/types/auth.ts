// Auth DTOs — match Flowamaz.Application.Auth.DTOs (camelCase JSON).

export interface UserSummary {
  id: string;
  email: string;
  name: string;
  orgId: string;
  orgSlug: string;
}

export interface WorkspaceSummary {
  id: string;
  slug: string;
  name: string;
  role: string;
}

/** GET /api/v1/auth/me */
export interface MeResponse {
  id: string;
  email: string;
  name: string;
  orgId: string;
  orgSlug: string;
  workspaces: WorkspaceSummary[];
}

/** POST /login and /register response body. */
export interface AuthResponse {
  accessToken: string;
  expiresIn: number;
  user: UserSummary;
}

/** POST /refresh response body (no user). */
export interface RefreshResponse {
  accessToken: string;
  expiresIn: number;
}

export interface LoginRequest {
  email: string;
  password: string;
  orgSlug: string;
}

export interface RegisterRequest {
  orgName: string;
  orgSlug: string;
  billingEmail: string;
  email: string;
  name: string;
  password: string;
  planSlug: string;
  dataRegion?: string;
}
