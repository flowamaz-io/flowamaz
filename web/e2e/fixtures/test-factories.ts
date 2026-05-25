import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { type APIRequestContext, type Page, request } from '@playwright/test';

const execFileAsync = promisify(execFile);

/**
 * Shared E2E fixtures. These talk to the backend API directly (not through the UI) to set up
 * state quickly and deterministically. Responses are wrapped in the AutoWrapper envelope
 * `{ success, statusCode, data, correlationId }`; tokens live in `data.accessToken` and the
 * refresh token is set as an httpOnly `fmz_refresh` cookie.
 */

export const API_URL = process.env.E2E_API_URL || 'http://localhost:5000';

const PASSWORD = 'Sup3rSecret!23';

export interface TestOrg {
  orgSlug: string;
  orgName: string;
  email: string;
  password: string;
  name: string;
  accessToken: string;
  orgId: string;
  userId: string;
}

export interface TestWorkspace {
  id: string;
  name: string;
  slug: string;
}

/** Unique, slug-safe suffix so parallel runs / reruns never collide. */
export function uniqueSuffix(): string {
  return `${Date.now().toString(36)}${Math.floor(Math.random() * 1e6).toString(36)}`.toLowerCase();
}

function unwrap<T>(body: unknown): T {
  const env = body as { data?: T };
  return env.data as T;
}

/**
 * Clears the backend's per-IP rate-limit counters so a serial test run can register/login many
 * times (the API caps registration at 5/hour and login at 10/min per IP, and all local test
 * traffic shares one IP). Best-effort: flushes the `ratelimit:*` Redis keys via redis-cli inside
 * the dev Redis container. Override the container/host with E2E_REDIS_CONTAINER, or disable
 * entirely with E2E_SKIP_RATELIMIT_RESET=1 (e.g. against an environment where you cannot reach
 * Redis — then keep the run small enough to stay under the limits).
 */
export async function resetRateLimits(): Promise<void> {
  if (process.env.E2E_SKIP_RATELIMIT_RESET === '1') return;
  const container = process.env.E2E_REDIS_CONTAINER || 'flowamaz-e2e-redis';
  try {
    // Delete all ratelimit:* keys (KEYS is fine for a tiny dev dataset).
    await execFileAsync('docker', [
      'exec',
      container,
      'sh',
      '-c',
      "redis-cli --scan --pattern 'ratelimit:*' | xargs -r redis-cli del >/dev/null 2>&1 || true",
    ]);
  } catch {
    // Non-fatal: if docker/redis isn't reachable this way, the run just relies on the real limits.
  }
}

/** Registers a brand-new org with a unique slug and returns its owner session. */
export async function createTestOrg(
  api: APIRequestContext,
  overrides: Partial<{ slug: string; plan: string; email: string }> = {},
): Promise<TestOrg> {
  await resetRateLimits();
  const suffix = uniqueSuffix();
  const orgSlug = overrides.slug ?? `e2e-${suffix}`;
  const email = overrides.email ?? `owner-${suffix}@e2e.flowamaz.test`;
  const orgName = `E2E Org ${suffix}`;
  const name = 'E2E Owner';

  const res = await api.post(`${API_URL}/api/v1/auth/register`, {
    data: {
      orgName,
      orgSlug,
      billingEmail: email,
      email,
      name,
      password: PASSWORD,
      planSlug: overrides.plan ?? 'starter',
    },
  });
  if (!res.ok()) {
    throw new Error(`createTestOrg failed (${res.status()}): ${await res.text()}`);
  }
  const data = unwrap<{ accessToken: string; user: { id: string; orgId: string } }>(await res.json());
  return {
    orgSlug,
    orgName,
    email,
    password: PASSWORD,
    name,
    accessToken: data.accessToken,
    orgId: data.user.orgId,
    userId: data.user.id,
  };
}

/** Creates a workspace for the given org owner token and returns its details. */
export async function createTestWorkspace(
  api: APIRequestContext,
  token: string,
  name = `WS ${uniqueSuffix()}`,
): Promise<TestWorkspace> {
  const slug = `ws-${uniqueSuffix()}`;
  const res = await api.post(`${API_URL}/api/v1/workspaces`, {
    headers: { Authorization: `Bearer ${token}` },
    data: { name, slug },
  });
  if (!res.ok()) {
    throw new Error(`createTestWorkspace failed (${res.status()}): ${await res.text()}`);
  }
  const ws = unwrap<{ id: string; name: string; slug: string }>(await res.json());
  return { id: ws.id, name: ws.name, slug: ws.slug };
}

/**
 * Re-logs-in via the API so the returned access token's `workspaces` claim includes any
 * workspace created after the original registration (the JWT membership snapshot is taken at
 * sign-in time — see JwtService). Returns the fresh access token.
 */
export async function loginViaApi(api: APIRequestContext, org: TestOrg): Promise<string> {
  await resetRateLimits();
  const res = await api.post(`${API_URL}/api/v1/auth/login`, {
    data: { email: org.email, password: org.password, orgSlug: org.orgSlug },
  });
  if (!res.ok()) {
    throw new Error(`loginViaApi failed (${res.status()}): ${await res.text()}`);
  }
  return unwrap<{ accessToken: string }>(await res.json()).accessToken;
}

/**
 * Drives the UI login form so the SPA holds a live session (access token in memory + refresh
 * cookie in the browser). Returns once the post-login navigation has settled.
 */
export async function loginViaUi(page: Page, org: TestOrg): Promise<void> {
  await resetRateLimits();
  await page.goto('/login');
  // FmInput labels also expose an aria-label on the inline help tooltip, so target the inputs by
  // their (unambiguous) placeholders rather than by accessible label.
  await page.getByPlaceholder('you@company.com').fill(org.email);
  await page.getByPlaceholder('acme').fill(org.orgSlug);
  await page.locator('input[type="password"]').fill(org.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
}

/**
 * Marks onboarding complete for an org in localStorage so the router's requiresOnboarding
 * guard lets the dashboard / settings routes render. Mirrors useOnboarding's storage shape.
 */
export async function seedOnboardingComplete(page: Page, orgId: string): Promise<void> {
  await page.addInitScript(
    ([id]) => {
      localStorage.setItem(
        `fmz_onboarding_${id}`,
        JSON.stringify({ step: 3, completed: true, workspaceCreated: true, memberInvited: false }),
      );
    },
    [orgId],
  );
}

/**
 * Full "ready to use the app" setup: registers an org, creates a workspace, re-logs-in so the
 * JWT carries the workspace membership, seeds onboarding-complete, and logs the SPA in.
 */
export async function setupReadyOrg(
  page: Page,
  options: { plan?: string } = {},
): Promise<{ org: TestOrg; workspace: TestWorkspace }> {
  const api = await request.newContext();
  try {
    const org = await createTestOrg(api, { plan: options.plan });
    const workspace = await createTestWorkspace(api, org.accessToken);
    await seedOnboardingComplete(page, org.orgId);
    await loginViaUi(page, org);
    return { org, workspace };
  } finally {
    await api.dispose();
  }
}
