import axios, { AxiosError, AxiosInstance, InternalAxiosRequestConfig } from "axios";
import { readConfig } from "./config";
import { getToken } from "./auth";
import { ensureFreshSession, refreshSession } from "./session";

/**
 * Build the Authorization header for the current session.
 * If FMZ_API_KEY is set it is used directly as the bearer value
 * (Flowamaz API keys are prefixed `fmz_`). Otherwise the stored
 * user token (keytar / credentials file) is used.
 * Returns {} when no token is available.
 */
export async function authHeader(): Promise<Record<string, string>> {
  if (process.env.FMZ_API_KEY && process.env.FMZ_API_KEY.length > 0) {
    return { Authorization: "Bearer " + process.env.FMZ_API_KEY };
  }
  const token = await getToken();
  if (token && token.length > 0) {
    return { Authorization: "Bearer " + token };
  }
  return {};
}

type RetriableConfig = InternalAxiosRequestConfig & { _fmzRetried?: boolean };

/**
 * Create an authenticated axios instance pointed at the configured API.
 * A signed-in CLI keeps itself signed in: the access token is refreshed just before it expires, and a request
 * refused with 401 is refreshed and retried once (e.g. the token expired mid-command).
 */
export async function createClient(): Promise<AxiosInstance> {
  const config = readConfig();
  await ensureFreshSession();
  const headers = await authHeader();
  const client = axios.create({
    baseURL: config.api_url,
    headers: {
      "Content-Type": "application/json",
      ...headers,
    },
    validateStatus: (status) => status >= 200 && status < 300,
  });

  client.interceptors.response.use(undefined, async (err: AxiosError) => {
    const original = err.config as RetriableConfig | undefined;
    if (!original || original._fmzRetried || err.response?.status !== 401) {
      throw err;
    }
    original._fmzRetried = true;
    if ((await refreshSession()) !== "refreshed") {
      throw err;
    }
    const fresh = await authHeader();
    if (fresh.Authorization) {
      original.headers.set("Authorization", fresh.Authorization);
      client.defaults.headers["Authorization"] = fresh.Authorization; // later requests in this command
    }
    return client.request(original);
  });

  return client;
}
