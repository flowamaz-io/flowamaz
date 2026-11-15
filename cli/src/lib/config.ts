import fs from "fs";
import os from "os";
import path from "path";

export interface FmzConfig {
  api_url: string;
  current_workspace?: string;
  current_org?: string;
  token_expiry?: string;
}

const DEFAULT_API_URL = "https://api.flowamaz.io";

export function configDir(): string {
  const override = process.env.FMZ_CONFIG_DIR;
  if (override && override.length > 0) {
    return override;
  }
  return path.join(os.homedir(), ".fmz");
}

function configPath(): string {
  return path.join(configDir(), "config.json");
}

function readConfigFile(): Record<string, unknown> {
  const file = configPath();
  if (!fs.existsSync(file)) {
    return {};
  }
  try {
    const parsed = JSON.parse(fs.readFileSync(file, "utf8")) as unknown;
    return parsed && typeof parsed === "object" && !Array.isArray(parsed)
      ? (parsed as Record<string, unknown>)
      : {};
  } catch {
    return {};
  }
}

/**
 * The effective config. api_url precedence: FMZ_API_URL → api_url saved in config.json → the default.
 * The environment wins so one command can point somewhere else (`FMZ_API_URL=… fmz …`) without editing the file.
 */
export function readConfig(): FmzConfig {
  const obj = readConfigFile();
  const env = process.env.FMZ_API_URL;
  const saved = typeof obj.api_url === "string" && obj.api_url.length > 0 ? obj.api_url : undefined;
  return {
    api_url: env && env.length > 0 ? env : saved ?? DEFAULT_API_URL,
    current_workspace:
      typeof obj.current_workspace === "string" ? obj.current_workspace : undefined,
    current_org: typeof obj.current_org === "string" ? obj.current_org : undefined,
    token_expiry: typeof obj.token_expiry === "string" ? obj.token_expiry : undefined,
  };
}

/**
 * Saves the given keys over what config.json already holds. Only keys passed (or already saved) are written, so an
 * api_url that came from FMZ_API_URL or the default is never persisted by accident.
 */
export function writeConfig(partial: Partial<FmzConfig>): FmzConfig {
  const dir = configDir();
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
  const merged: Record<string, unknown> = { ...readConfigFile(), ...partial };
  for (const key of Object.keys(merged)) {
    if (merged[key] === undefined) {
      delete merged[key];
    }
  }
  fs.writeFileSync(configPath(), JSON.stringify(merged, null, 2), "utf8");
  return readConfig();
}
