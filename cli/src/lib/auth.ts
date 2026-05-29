import fs from "fs";
import path from "path";
import { configDir } from "./config";

const KEYTAR_SERVICE = "flowamaz-cli";
const KEYTAR_ACCOUNT = "default";

interface KeytarLike {
  getPassword(service: string, account: string): Promise<string | null>;
  setPassword(service: string, account: string, password: string): Promise<void>;
  deletePassword(service: string, account: string): Promise<boolean>;
}

function loadKeytar(): KeytarLike | null {
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

function readCredentialsFile(): string | null {
  const file = credentialsPath();
  if (!fs.existsSync(file)) {
    return null;
  }
  try {
    const parsed = JSON.parse(fs.readFileSync(file, "utf8")) as unknown;
    if (parsed && typeof parsed === "object") {
      const token = (parsed as Record<string, unknown>).token;
      return typeof token === "string" && token.length > 0 ? token : null;
    }
    return null;
  } catch {
    return null;
  }
}

function writeCredentialsFile(token: string): void {
  const dir = configDir();
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
  fs.writeFileSync(credentialsPath(), JSON.stringify({ token }, null, 2), "utf8");
}

function clearCredentialsFile(): void {
  const file = credentialsPath();
  if (fs.existsSync(file)) {
    fs.unlinkSync(file);
  }
}

/**
 * Resolve the current bearer token.
 * Priority: FMZ_API_KEY env (CI mode) → keytar → credentials file.
 */
export async function getToken(): Promise<string | null> {
  if (process.env.FMZ_API_KEY && process.env.FMZ_API_KEY.length > 0) {
    return process.env.FMZ_API_KEY;
  }
  const keytar = loadKeytar();
  if (keytar) {
    try {
      const stored = await keytar.getPassword(KEYTAR_SERVICE, KEYTAR_ACCOUNT);
      if (stored && stored.length > 0) {
        return stored;
      }
    } catch {
      // fall through to file
    }
  }
  return readCredentialsFile();
}

export async function setToken(token: string): Promise<void> {
  const keytar = loadKeytar();
  if (keytar) {
    try {
      await keytar.setPassword(KEYTAR_SERVICE, KEYTAR_ACCOUNT, token);
      return;
    } catch {
      // fall through to file
    }
  }
  writeCredentialsFile(token);
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
