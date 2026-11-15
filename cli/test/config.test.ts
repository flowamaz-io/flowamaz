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

  it("does not save an api_url that came from FMZ_API_URL or the default", () => {
    process.env.FMZ_API_URL = "http://localhost:8307";
    writeConfig({ token_expiry: "2026-11-14T10:00:00Z" });
    const parsed = JSON.parse(fs.readFileSync(path.join(tmpDir, "config.json"), "utf8"));
    expect(parsed.api_url).toBeUndefined();
  });

  it("lets FMZ_API_URL override a saved api_url", () => {
    fs.writeFileSync(path.join(tmpDir, "config.json"), JSON.stringify({ api_url: "http://saved:1" }));
    expect(readConfig().api_url).toBe("http://saved:1");
    process.env.FMZ_API_URL = "http://localhost:1";
    expect(readConfig().api_url).toBe("http://localhost:1");
  });

  it("removes keys written as undefined", () => {
    writeConfig({ token_expiry: "x" });
    writeConfig({ token_expiry: undefined });
    const parsed = JSON.parse(fs.readFileSync(path.join(tmpDir, "config.json"), "utf8"));
    expect("token_expiry" in parsed).toBe(false);
  });
});

