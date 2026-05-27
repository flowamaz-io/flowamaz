import axios, {
  type AxiosInstance,
  type AxiosResponse,
  type InternalAxiosRequestConfig,
} from 'axios';
import type { ApiEnvelope, RefreshResponse } from '@/types';

const BASE_URL = import.meta.env.VITE_API_BASE_URL || '';

/**
 * Lazily-wired hooks so the Axios instance never imports the Pinia store directly
 * (which would create a circular dependency: store → service → store).
 */
interface AuthBridge {
  getToken: () => string | null;
  setToken: (token: string) => void;
  onAuthFailure: () => Promise<void> | void;
}

let bridge: AuthBridge | null = null;

export function registerAuthBridge(b: AuthBridge): void {
  bridge = b;
}

export const http: AxiosInstance = axios.create({
  baseURL: BASE_URL,
  withCredentials: true, // send the httpOnly fmz_refresh cookie on refresh calls
  headers: { 'Content-Type': 'application/json' },
});

// ── Request: attach bearer token ──────────────────────────────────────────────
http.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const token = bridge?.getToken();
  if (token) {
    config.headers.set('Authorization', `Bearer ${token}`);
  }
  return config;
});

// ── Response: 401 → refresh → retry exactly once ──────────────────────────────
interface RetriableConfig extends InternalAxiosRequestConfig {
  _retried?: boolean;
}

let refreshInFlight: Promise<string | null> | null = null;

/** Calls the refresh endpoint directly (bypasses interceptors) so we don't recurse. */
async function performRefresh(): Promise<string | null> {
  try {
    const res = await axios.post<ApiEnvelope<RefreshResponse>>(
      `${BASE_URL}/api/v1/auth/refresh`,
      {},
      { withCredentials: true },
    );
    const token = res.data?.data?.accessToken ?? null;
    if (token) bridge?.setToken(token);
    return token;
  } catch {
    return null;
  }
}

http.interceptors.response.use(
  (response: AxiosResponse) => response,
  async (error: unknown) => {
    if (!axios.isAxiosError(error) || !error.config) {
      return Promise.reject(error);
    }
    const original = error.config as RetriableConfig;
    const status = error.response?.status;
    const isAuthEndpoint = original.url?.includes('/api/v1/auth/');

    if (status === 401 && !original._retried && !isAuthEndpoint) {
      original._retried = true;
      refreshInFlight ??= performRefresh().finally(() => {
        refreshInFlight = null;
      });
      const token = await refreshInFlight;
      if (token) {
        original.headers.set('Authorization', `Bearer ${token}`);
        return http(original);
      }
      await bridge?.onAuthFailure();
    }
    return Promise.reject(error);
  },
);

/** Unwraps the `{ success, data, ... }` envelope and returns the payload. */
export function unwrap<T>(response: AxiosResponse<ApiEnvelope<T>>): T {
  return response.data.data;
}

export { BASE_URL };
