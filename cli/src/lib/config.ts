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

function defaultApiUrl(): string {
  return process.env.FMZ_API_URL && process.env.FMZ_API_URL.length > 0
    ? process.env.FMZ_API_URL
    : DEFAULT_API_URL;
}

export function readConfig(): FmzConfig {
  const base: FmzConfig = { api_url: defaultApiUrl() };
  const file = configPath();
  if (!fs.existsSync(file)) {
    return base;
  }
  try {
    const raw = fs.readFileSync(file, "utf8");
    const parsed = JSON.parse(raw) as unknown;
    if (parsed && typeof parsed === "object") {
      const obj = parsed as Record<string, unknown>;
      return {
        api_url:
          typeof obj.api_url === "string" && obj.api_url.length > 0
            ? obj.api_url
            : base.api_url,
        current_workspace:
          typeof obj.current_workspace === "string"
            ? obj.current_workspace
            : undefined,
        current_org:
          typeof obj.current_org === "string" ? obj.current_org : undefined,
        token_expiry:
          typeof obj.token_expiry === "string" ? obj.token_expiry : undefined,
      };
    }
    return base;
  } catch {
    return base;
  }
}

export function writeConfig(partial: Partial<FmzConfig>): FmzConfig {
  const dir = configDir();
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
  const current = readConfig();
  const merged: FmzConfig = { ...current, ...partial };
  fs.writeFileSync(configPath(), JSON.stringify(merged, null, 2), "utf8");
  return merged;
}
