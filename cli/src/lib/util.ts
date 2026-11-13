import axios from "axios";
import { readConfig } from "./config";

/**
 * Turn any thrown value (axios error, Error, unknown) into a readable,
 * actionable message. Never throws.
 */
export function describeError(err: unknown): string {
  if (axios.isAxiosError(err)) {
    const status = err.response?.status;
    const data = err.response?.data as unknown;
    let detail = "";
    if (data && typeof data === "object") {
      const obj = data as Record<string, unknown>;
      const m = obj.message ?? obj.error ?? obj.title;
      if (typeof m === "string") {
        detail = m;
      }
    } else if (typeof data === "string") {
      detail = data;
    }
    if (status === 401) {
      return "Not authenticated. Run: fmz auth login";
    }
    if (status === 404) {
      // The API's 404s say what is missing and what to do; only a bare 404 means the route itself is missing.
      return detail
        ? "Not found (404): " + detail
        : "Resource not found (404). The endpoint may not be available on this backend.";
    }
    if (status) {
      return "Request failed (" + status + ")" + (detail ? ": " + detail : "");
    }
    if (err.code === "ECONNREFUSED" || err.code === "ENOTFOUND") {
      return (
        "Could not reach the API at " +
        readConfig().api_url +
        ". Check FMZ_API_URL or your network."
      );
    }
    return err.message;
  }
  if (err instanceof Error) {
    return err.message;
  }
  return String(err);
}

/**
 * Resolve the current workspace slug, exiting with an actionable error
 * when none is selected.
 */
export function requireWorkspace(override?: string): string {
  if (override && override.length > 0) {
    return override;
  }
  if (process.env.FMZ_WORKSPACE && process.env.FMZ_WORKSPACE.length > 0) {
    return process.env.FMZ_WORKSPACE;
  }
  const ws = readConfig().current_workspace;
  if (ws && ws.length > 0) {
    return ws;
  }
  throw new Error(
    "No workspace selected. Run: fmz workspace use <slug> (or set FMZ_WORKSPACE)"
  );
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * The selected workspace's id. The API addresses workspaces by id; `fmz workspace use` and FMZ_WORKSPACE
 * accept a slug too, which is looked up in the workspaces this account can see.
 */
export async function requireWorkspaceId(override?: string): Promise<string> {
  const value = requireWorkspace(override);
  if (GUID.test(value)) return value;
  // Imported here so util stays free of the HTTP client for its synchronous helpers.
  const { createClient } = await import("./api-client");
  const client = await createClient();
  const resp = await client.get("/api/v1/workspaces", { params: { pageSize: 100 } });
  const match = asArray(resp.data).find((w) => w.slug === value || w.id === value);
  if (!match || typeof match.id !== "string") {
    throw new Error(
      "No workspace '" + value + "' in this account. Run `fmz workspace list` to see the slugs, then `fmz workspace use <slug>`."
    );
  }
  return match.id;
}

/**
 * Coerce an unknown API payload into an array of records for table rendering.
 * Handles both bare arrays and AutoWrapper-style { result: [...] } envelopes.
 */
export function asArray(data: unknown): Record<string, unknown>[] {
  let value: unknown = data;
  // Envelopes nest: { success, data: [...] } or, for paged lists, { success, data: { data: [...], pagination } }.
  for (let depth = 0; depth < 3 && value && typeof value === "object" && !Array.isArray(value); depth++) {
    const obj = value as Record<string, unknown>;
    const next = obj.result ?? obj.data ?? obj.items;
    if (next === undefined) break;
    value = next;
  }
  if (Array.isArray(value)) {
    return value.filter(
      (v): v is Record<string, unknown> => v !== null && typeof v === "object"
    );
  }
  return [];
}

/** Unwrap an AutoWrapper-style { result: T } envelope, else return as-is. */
export function unwrap(data: unknown): unknown {
  if (data && typeof data === "object" && !Array.isArray(data)) {
    const obj = data as Record<string, unknown>;
    if ("result" in obj) {
      return obj.result;
    }
    // AutoWrapper envelope: { success, statusCode, data }.
    if ("data" in obj && ("success" in obj || "statusCode" in obj)) {
      return obj.data;
    }
  }
  return data;
}

export function str(value: unknown): string {
  if (value === null || value === undefined) {
    return "";
  }
  if (typeof value === "string") {
    return value;
  }
  return String(value);
}
