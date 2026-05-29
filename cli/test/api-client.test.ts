import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { authHeader } from "../src/lib/api-client";

describe("authHeader", () => {
  const original = process.env.FMZ_API_KEY;

  beforeEach(() => {
    delete process.env.FMZ_API_KEY;
  });

  afterEach(() => {
    if (original === undefined) {
      delete process.env.FMZ_API_KEY;
    } else {
      process.env.FMZ_API_KEY = original;
    }
  });

  it("uses FMZ_API_KEY as the bearer token when set", async () => {
    process.env.FMZ_API_KEY = "fmz_test_key";
    const header = await authHeader();
    expect(header.Authorization).toBe("Bearer fmz_test_key");
  });

  it("FMZ_API_KEY takes precedence over any stored user token", async () => {
    // Point at an isolated empty config dir so no credentials file leaks in.
    process.env.FMZ_CONFIG_DIR = "/tmp/fmz-test-nonexistent-" + Date.now();
    process.env.FMZ_API_KEY = "fmz_ci_key";
    const header = await authHeader();
    expect(header.Authorization).toBe("Bearer fmz_ci_key");
    delete process.env.FMZ_CONFIG_DIR;
  });

  it("returns an empty object when no token is available", async () => {
    process.env.FMZ_CONFIG_DIR = "/tmp/fmz-test-empty-" + Date.now();
    const header = await authHeader();
    expect(header).toEqual({});
    delete process.env.FMZ_CONFIG_DIR;
  });
});
