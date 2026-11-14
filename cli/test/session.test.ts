import { describe, it, expect, beforeEach, afterEach } from "vitest";
import fs from "fs";
import http from "http";
import os from "os";
import path from "path";
import { AddressInfo } from "net";
import { getCredentials, parseCredentials, setCredentials } from "../src/lib/auth";
import { createClient } from "../src/lib/api-client";
import { needsRefresh, refreshSession } from "../src/lib/session";
import { logout, clientName } from "../src/commands/auth";
import { setQuiet } from "../src/lib/output";

interface Seen {
  method: string;
  url: string;
  auth?: string;
  body: string;
}

/** A tiny fake API: refresh hands out "access-N"; /api/v1/thing wants the newest token. */
function fakeApi(opts: { refreshStatus?: number; thingAccepts?: () => string } = {}) {
  const seen: Seen[] = [];
  let issued = 0;
  const server = http.createServer((req, res) => {
    let body = "";
    req.on("data", (c) => (body += c));
    req.on("end", () => {
      seen.push({ method: req.method ?? "", url: req.url ?? "", auth: req.headers.authorization, body });
      const send = (status: number, data: unknown) => {
        res.writeHead(status, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ success: status < 400, statusCode: status, data }));
      };
      if (req.url === "/api/v1/cli/auth/refresh") {
        if ((opts.refreshStatus ?? 200) !== 200) return send(opts.refreshStatus ?? 401, { message: "ended" });
        issued++;
        return send(200, {
          accessToken: "access-" + issued,
          expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
          sessionId: "s-1",
        });
      }
      if (req.url === "/api/v1/cli/auth/logout") return send(200, { loggedOut: true, sessionEnded: true });
      if (req.url === "/api/v1/thing") {
        const wanted = opts.thingAccepts ? opts.thingAccepts() : "Bearer access-" + issued;
        return req.headers.authorization === wanted ? send(200, { ok: true }) : send(401, { message: "no" });
      }
      send(404, { message: "nope" });
    });
  });
  return { server, seen };
}

describe("CLI sessions", () => {
  let tmpDir: string;
  let server: http.Server;
  let seen: Seen[];

  async function start(opts: Parameters<typeof fakeApi>[0] = {}): Promise<void> {
    const api = fakeApi(opts);
    server = api.server;
    seen = api.seen;
    await new Promise<void>((r) => server.listen(0, "127.0.0.1", () => r()));
    process.env.FMZ_API_URL = "http://127.0.0.1:" + (server.address() as AddressInfo).port;
  }

  beforeEach(() => {
    tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), "fmz-session-"));
    process.env.FMZ_CONFIG_DIR = tmpDir;
    process.env.FMZ_CREDENTIAL_STORE = "file";
    delete process.env.FMZ_API_KEY;
    setQuiet(true);
  });

  afterEach(async () => {
    if (server) await new Promise<void>((r) => server.close(() => r()));
    delete process.env.FMZ_CONFIG_DIR;
    delete process.env.FMZ_CREDENTIAL_STORE;
    delete process.env.FMZ_API_URL;
    setQuiet(false);
    fs.rmSync(tmpDir, { recursive: true, force: true });
  });

  it("reads JSON credentials and the bare token older CLIs stored", () => {
    expect(parseCredentials("abc.def")).toEqual({ token: "abc.def" });
    expect(parseCredentials('{"token":"t","refreshToken":"r"}')).toMatchObject({ token: "t", refreshToken: "r" });
    expect(parseCredentials('{"refreshToken":"r"}')).toBeNull();
    expect(parseCredentials("")).toBeNull();
  });

  it("stores credentials owner-only", async () => {
    await setCredentials({ token: "t", refreshToken: "r" });
    const mode = fs.statSync(path.join(tmpDir, "credentials.json")).mode & 0o777;
    if (process.platform !== "win32") expect(mode).toBe(0o600);
  });

  it("refreshes only sign-ins with a refresh token that are near expiry", () => {
    const now = Date.now();
    expect(needsRefresh({ token: "t" }, now)).toBe(false);
    expect(needsRefresh({ token: "t", refreshToken: "r" }, now)).toBe(true);
    expect(needsRefresh({ token: "t", refreshToken: "r", expiresAt: new Date(now + 30_000).toISOString() }, now)).toBe(true);
    expect(needsRefresh({ token: "t", refreshToken: "r", expiresAt: new Date(now + 10 * 60_000).toISOString() }, now)).toBe(false);
  });

  it("refreshes an expiring token before the request", async () => {
    await start();
    await setCredentials({ token: "old", refreshToken: "r", expiresAt: new Date(Date.now() - 1000).toISOString() });

    const client = await createClient();
    const resp = await client.get("/api/v1/thing");

    expect(resp.status).toBe(200);
    expect(seen[0].url).toBe("/api/v1/cli/auth/refresh");
    expect(JSON.parse(seen[0].body)).toEqual({ refreshToken: "r" });
    expect((await getCredentials())?.token).toBe("access-1");
    expect((await getCredentials())?.refreshToken).toBe("r");
  });

  it("refreshes and retries once when a request gets 401", async () => {
    await start();
    // Looks fresh locally, but the server refuses it (e.g. the clock is off).
    await setCredentials({ token: "stale", refreshToken: "r", expiresAt: new Date(Date.now() + 10 * 60_000).toISOString() });

    const client = await createClient();
    const resp = await client.get("/api/v1/thing");

    expect(resp.status).toBe(200);
    expect(seen.map((s) => s.url)).toEqual(["/api/v1/thing", "/api/v1/cli/auth/refresh", "/api/v1/thing"]);
    expect(seen[2].auth).toBe("Bearer access-1");
  });

  it("does not loop when the retried request is refused again", async () => {
    await start({ thingAccepts: () => "never" });
    await setCredentials({ token: "stale", refreshToken: "r", expiresAt: new Date(Date.now() + 10 * 60_000).toISOString() });

    const client = await createClient();
    await expect(client.get("/api/v1/thing")).rejects.toMatchObject({ response: { status: 401 } });
    expect(seen.filter((s) => s.url === "/api/v1/thing")).toHaveLength(2);
  });

  it("forgets the sign-in when the server says the session has ended", async () => {
    await start({ refreshStatus: 401 });
    await setCredentials({ token: "t", refreshToken: "r" });

    expect(await refreshSession()).toBe("ended");
    expect(await getCredentials()).toBeNull();
  });

  it("keeps the sign-in when the API can't be reached", async () => {
    process.env.FMZ_API_URL = "http://127.0.0.1:1";
    await setCredentials({ token: "t", refreshToken: "r" });

    expect(await refreshSession()).toBe("unavailable");
    expect((await getCredentials())?.token).toBe("t");
  });

  it("never refreshes when FMZ_API_KEY is set", async () => {
    process.env.FMZ_API_KEY = "fmz_ci";
    await setCredentials({ token: "t", refreshToken: "r" });
    expect(await refreshSession()).toBe("not-applicable");
    delete process.env.FMZ_API_KEY;
  });

  it("fmz auth logout ends the session on the server, then forgets it", async () => {
    await start();
    await setCredentials({ token: "t", refreshToken: "r" });

    await logout();

    expect(seen).toHaveLength(1);
    expect(seen[0].url).toBe("/api/v1/cli/auth/logout");
    expect(seen[0].auth).toBe("Bearer t");
    expect(JSON.parse(seen[0].body)).toEqual({ refreshToken: "r" });
    expect(await getCredentials()).toBeNull();
  });

  it("fmz auth logout still forgets the sign-in when the API is unreachable", async () => {
    process.env.FMZ_API_URL = "http://127.0.0.1:1";
    await setCredentials({ token: "t", refreshToken: "r" });

    await logout();

    expect(await getCredentials()).toBeNull();
  });

  it("names the client after the host, or FMZ_CLIENT_NAME", () => {
    expect(clientName()).toContain(os.hostname().slice(0, 10));
    process.env.FMZ_CLIENT_NAME = "ci-runner-3";
    expect(clientName()).toBe("ci-runner-3");
    delete process.env.FMZ_CLIENT_NAME;
  });
});
