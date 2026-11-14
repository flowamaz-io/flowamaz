import axios from "axios";
import { readConfig, writeConfig } from "./config";
import { clearToken, getCredentials, setCredentials, StoredCredentials } from "./auth";
import { unwrap } from "./util";
import { warn } from "./output";

/** Refresh this long before the access token runs out, so a request never starts with a token about to expire. */
export const REFRESH_MARGIN_MS = 60_000;

interface RefreshResponse {
  accessToken: string;
  expiresAt?: string;
  sessionId?: string;
}

export type RefreshOutcome = "refreshed" | "ended" | "unavailable" | "not-applicable";

function usingApiKey(): boolean {
  return !!process.env.FMZ_API_KEY && process.env.FMZ_API_KEY.length > 0;
}

/** True when the stored access token is missing an expiry or expires within the margin. */
export function needsRefresh(creds: StoredCredentials, now: number = Date.now()): boolean {
  if (!creds.refreshToken) {
    return false;
  }
  if (!creds.expiresAt) {
    return true;
  }
  const expires = Date.parse(creds.expiresAt);
  return Number.isNaN(expires) || expires - now <= REFRESH_MARGIN_MS;
}

/**
 * Trades the CLI session's refresh token for a new access token and stores it.
 * - "ended": the server says the session is over (logged out, revoked in Settings, or idle too long). The stored
 *   sign-in is removed so the next command says to run `fmz auth login`.
 * - "unavailable": the API couldn't be reached; the stored sign-in is kept as it is.
 */
export async function refreshSession(): Promise<RefreshOutcome> {
  if (usingApiKey()) {
    return "not-applicable";
  }
  const creds = await getCredentials();
  if (!creds?.refreshToken) {
    return "not-applicable";
  }
  try {
    const resp = await axios.post(
      readConfig().api_url + "/api/v1/cli/auth/refresh",
      { refreshToken: creds.refreshToken },
      { headers: { "Content-Type": "application/json" }, validateStatus: (s) => s === 200 || s === 401 }
    );
    if (resp.status === 401) {
      await clearToken();
      writeConfig({ token_expiry: undefined });
      warn(
        "Your CLI session has ended: it was signed out, revoked in Settings → CLI sessions, or not used for a long time. Run: fmz auth login"
      );
      return "ended";
    }
    const data = unwrap(resp.data) as RefreshResponse;
    await setCredentials({
      ...creds,
      token: data.accessToken,
      expiresAt: data.expiresAt,
      sessionId: data.sessionId ?? creds.sessionId,
    });
    if (data.expiresAt) {
      writeConfig({ token_expiry: data.expiresAt });
    }
    return "refreshed";
  } catch {
    return "unavailable";
  }
}

/** Refreshes first when the stored access token is about to expire. No-op for API keys and older sign-ins. */
export async function ensureFreshSession(): Promise<void> {
  if (usingApiKey()) {
    return;
  }
  const creds = await getCredentials();
  if (creds && needsRefresh(creds)) {
    await refreshSession();
  }
}
