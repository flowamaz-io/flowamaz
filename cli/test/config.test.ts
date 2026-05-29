import { describe, it, expect, beforeEach, afterEach } from "vitest";
import fs from "fs";
import os from "os";
import path from "path";
import { readConfig, writeConfig } from "../src/lib/config";

describe("config", () => {
  let tmpDir: string;
  const originalApiUrl = process.env.FMZ_API_URL;

  beforeEach(() => {
    tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), "fmz-config-"));
    process.env.FMZ_CONFIG_DIR = tmpDir;
    delete process.env.FMZ_API_URL;
  });

  afterEach(() => {
    delete process.env.FMZ_CONFIG_DIR;
    if (originalApiUrl === undefined) {
      delete process.env.FMZ_API_URL;
    } else {
      process.env.FMZ_API_URL = originalApiUrl;
    }
    fs.rmSync(tmpDir, { recursive: true, force: true });
  });

  it("returns the default api_url when no config exists", () => {
    const cfg = readConfig();
    expect(cfg.api_url).toBe("https://api.flowamaz.io");
    expect(cfg.current_workspace).toBeUndefined();
  });

  it("honors FMZ_API_URL as the default", () => {
    process.env.FMZ_API_URL = "http://localhost:5000";
    expect(readConfig().api_url).toBe("http://localhost:5000");
  });

  it("writes and merges partial config, creating the dir", () => {
    fs.rmSync(tmpDir, { recursive: true, force: true });
    writeConfig({ current_workspace: "ops" });
    let cfg = readConfig();
    expect(cfg.current_workspace).toBe("ops");

    writeConfig({ current_org: "acme" });
    cfg = readConfig();
    expect(cfg.current_workspace).toBe("ops");
    expect(cfg.current_org).toBe("acme");
  });

  it("persists to config.json under the override dir", () => {
    writeConfig({ current_workspace: "demo" });
    const file = path.join(tmpDir, "config.json");
    expect(fs.existsSync(file)).toBe(true);
    const parsed = JSON.parse(fs.readFileSync(file, "utf8"));
    expect(parsed.current_workspace).toBe("demo");
  });
});
