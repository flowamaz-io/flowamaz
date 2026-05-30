// Workspace DTOs — match Flowamaz.Application.Workspace.DTOs (camelCase, string enums).

export type WorkspaceRole = 'Viewer' | 'Runner' | 'Operator' | 'Designer' | 'Admin';

export type MarketplacePolicy = 'AllowAll' | 'OfficialAndVerified' | 'Allowlist';

export interface WorkspaceSettings {
  maxConcurrentRuns: number;
  runRetentionDays: number;
  aiCostBudgetMonthUsd: number;
  allowedAiProviders: string[];
  defaultAiModelOverrides: Record<string, string>;
  marketplacePolicy: MarketplacePolicy;
}

/** GET /api/v1/workspaces/{id}/usage — plan/edition usage (prompt 05-07). */
export interface UsageResponse {
  edition: string;
  isCommunity: boolean;
  workflowsUsed: number;
  workflowsLimit: number;
  runsUsed: number;
  runsLimit: number;
  membersUsed: number;
  membersLimit: number;
  isAtLimit: boolean;
}

/** GET /api/v1/workspaces list item. */
export interface WorkspaceListItem {
  id: string;
  name: string;
  slug: string;
  role: string;
}

/** Full workspace response (create / get / update settings). */
export interface WorkspaceResponse {
  id: string;
  orgId: string;
  name: string;
  slug: string;
  settings: WorkspaceSettings;
  createdAt: string;
  /** Platform edition (community | starter | pro | enterprise), driven by the EDITION env var. */
  edition: string;
}

export interface CreateWorkspaceRequest {
  name: string;
  slug: string;
}

/** GET /api/v1/workspaces/{id}/environments item. Name is Dev / Staging / Production. */
export interface WorkspaceEnvironment {
  id: string;
  name: string;
  workspaceId: string;
  createdAt: string;
}

export interface UpdateWorkspaceSettingsRequest {
  maxConcurrentRuns: number;
  runRetentionDays: number;
  aiCostBudgetMonthUsd: number;
  allowedAiProviders: string[];
  defaultAiModelOverrides: Record<string, string>;
  marketplacePolicy: MarketplacePolicy;
}

// ── Members ──────────────────────────────────────────────────────────────────

export interface MemberResponse {
  orgUserId: string;
  email: string;
  name: string;
  role: WorkspaceRole;
  joinedAt: string;
  isActive: boolean;
}

export interface AddMemberRequest {
  email: string;
  role: WorkspaceRole;
}

export interface UpdateRoleRequest {
  role: WorkspaceRole;
}

// ── API keys ──────────────────────────────────────────────────────────────────

export interface ApiKeyResponse {
  id: string;
  environmentId: string;
  name: string;
  keyPrefix: string;
  scopes: string[];
  lastUsedAt: string | null;
  expiresAt: string | null;
  isActive: boolean;
  createdAt: string;
  /** Non-null ONLY on the create response (shown once). */
  plainKey: string | null;
}

export interface CreateApiKeyRequest {
  name: string;
  environmentId: string;
  scopes: string[];
  expiresAt: string | null;
}

// ── AI config ─────────────────────────────────────────────────────────────────

export interface AiFunctionResolution {
  functionId: string;
  modelId: string;
  provider: string;
  resolvedFrom: string;
}

export interface AiBudgetView {
  monthlyTokenLimit: number;
  tokensUsedThisMonth: number;
  budgetResetDate: string;
  isHardCapped: boolean;
}

export interface AiConfigView {
  allowedProviders: string[];
  budget: AiBudgetView | null;
  functions: AiFunctionResolution[];
}

export interface FunctionOverrideRequest {
  functionId: string;
  provider: string;
  modelId: string;
  keySource: string;
}

export interface UpdateAiConfigRequest {
  overrides: FunctionOverrideRequest[];
}
