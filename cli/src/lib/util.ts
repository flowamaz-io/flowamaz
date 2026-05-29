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
      return (
        "Resource not found (404)" +
        (detail ? ": " + detail : "") +
        ". The endpoint may not be available on this backend."
      );
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

/**
 * Coerce an unknown API payload into an array of records for table rendering.
 * Handles both bare arrays and AutoWrapper-style { result: [...] } envelopes.
 */
export function asArray(data: unknown): Record<string, unknown>[] {
  let value: unknown = data;
  if (value && typeof value === "object" && !Array.isArray(value)) {
    const obj = value as Record<string, unknown>;
    if (Array.isArray(obj.result)) {
      value = obj.result;
    } else if (Array.isArray(obj.data)) {
      value = obj.data;
    } else if (Array.isArray(obj.items)) {
      value = obj.items;
    }
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
