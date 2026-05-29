import axios, { AxiosInstance } from "axios";
import { readConfig } from "./config";
import { getToken } from "./auth";

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

/**
 * Create an authenticated axios instance pointed at the configured API.
 */
export async function createClient(): Promise<AxiosInstance> {
  const config = readConfig();
  const headers = await authHeader();
  return axios.create({
    baseURL: config.api_url,
    headers: {
      "Content-Type": "application/json",
      ...headers,
    },
    validateStatus: (status) => status >= 200 && status < 300,
  });
}
