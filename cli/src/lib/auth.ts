import fs from "fs";
import path from "path";
import { configDir } from "./config";

const KEYTAR_SERVICE = "flowamaz-cli";
const KEYTAR_ACCOUNT = "default";

/**
 * What `fmz auth login` stores. `token` is the 15-minute access token; `refreshToken` is the CLI session's
 * long-lived token, traded for new access tokens (POST /api/v1/cli/auth/refresh) and ended by `fmz auth logout`
 * or from Settings → CLI sessions. Sign-ins from older CLIs stored only `token`.
 */
export interface StoredCredentials {
  token: string;
  refreshToken?: string;
  expiresAt?: string;
  sessionId?: string;
}

interface KeytarLike {
  getPassword(service: string, account: string): Promise<string | null>;
  setPassword(service: string, account: string, password: string): Promise<void>;
  deletePassword(service: string, account: string): Promise<boolean>;
}

function loadKeytar(): KeytarLike | null {
  // FMZ_CREDENTIAL_STORE=file keeps credentials in ~/.fmz/credentials.json (tests, headless boxes without a keychain).
  if (process.env.FMZ_CREDENTIAL_STORE === "file") {
    return null;
  }
  try {
    // Dynamic optional require — keytar is an optionalDependency with a native
    // build that may be absent. Never import at top level.

    const mod = require("keytar") as KeytarLike;
    if (mod && typeof mod.getPassword === "function") {
      return mod;
    }
    return null;
  } catch {
    return null;
  }
}

function credentialsPath(): string {
  return path.join(configDir(), "credentials.json");
}

/** Accepts the JSON written now and the bare access token older CLIs kept in the keychain. */
export function parseCredentials(raw: string | null | undefined): StoredCredentials | null {
  if (!raw || raw.length === 0) {
    return null;
  }
  const trimmed = raw.trim();
  if (!trimmed.startsWith("{")) {
    return { token: trimmed };
  }
  try {
    const parsed = JSON.parse(trimmed) as unknown;
    if (!parsed || typeof parsed !== "object") {
      return null;
    }
    const obj = parsed as Record<string, unknown>;
    if (typeof obj.token !== "string" || obj.token.length === 0) {
      return null;
    }
    return {
      token: obj.token,
      refreshToken: typeof obj.refreshToken === "string" && obj.refreshToken.length > 0 ? obj.refreshToken : undefined,
      expiresAt: typeof obj.expiresAt === "string" ? obj.expiresAt : undefined,
      sessionId: typeof obj.sessionId === "string" ? obj.sessionId : undefined,
    };
  } catch {
    return null;
  }
}

function readCredentialsFile(): StoredCredentials | null {
  const file = credentialsPath();
  if (!fs.existsSync(file)) {
    return null;
  }
  try {
    return parseCredentials(fs.readFileSync(file, "utf8"));
  } catch {
    return null;
  }
}

function writeCredentialsFile(creds: StoredCredentials): void {
  const dir = configDir();
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true, mode: 0o700 });
  }
  // Owner read/write only: the refresh token keeps this machine signed in.
  fs.writeFileSync(credentialsPath(), JSON.stringify(creds, null, 2), { encoding: "utf8", mode: 0o600 });
  try {
    fs.chmodSync(credentialsPath(), 0o600);
  } catch {
    // Best effort (e.g. Windows).
  }
}

function clearCredentialsFile(): void {
  const file = credentialsPath();
  if (fs.existsSync(file)) {
    fs.unlinkSync(file);
  }
}

/**
 * The stored sign-in, or null. FMZ_API_KEY (CI mode) is not a stored sign-in; see getToken.
 * Priority: keytar → credentials file.
 */
export async function getCredentials(): Promise<StoredCredentials | null> {
  const keytar = loadKeytar();
  if (keytar) {
    try {
      const stored = parseCredentials(await keytar.getPassword(KEYTAR_SERVICE, KEYTAR_ACCOUNT));
      if (stored) {
        return stored;
      }
    } catch {
      // fall through to file
    }
  }
  return readCredentialsFile();
}

export async function setCredentials(creds: StoredCredentials): Promise<void> {
  const keytar = loadKeytar();
  if (keytar) {
    try {
      await keytar.setPassword(KEYTAR_SERVICE, KEYTAR_ACCOUNT, JSON.stringify(creds));
      // Don't leave an older file copy behind once the keychain holds it.
      clearCredentialsFile();
      return;
    } catch {
      // fall through to file
    }
  }
  writeCredentialsFile(creds);
}

/**
 * Resolve the current bearer token.
 * Priority: FMZ_API_KEY env (CI mode) → keytar → credentials file.
 */
export async function getToken(): Promise<string | null> {
  if (process.env.FMZ_API_KEY && process.env.FMZ_API_KEY.length > 0) {
    return process.env.FMZ_API_KEY;
  }
  return (await getCredentials())?.token ?? null;
}

/** Stores a bare access token (kept for callers that only have one). */
export async function setToken(token: string): Promise<void> {
  await setCredentials({ token });
}

export async function clearToken(): Promise<void> {
  const keytar = loadKeytar();
  if (keytar) {
    try {
      await keytar.deletePassword(KEYTAR_SERVICE, KEYTAR_ACCOUNT);
    } catch {
      // ignore
    }
  }
  clearCredentialsFile();
}
