import axios from "axios";
import os from "os";
import { Command } from "commander";
import open from "open";
import { createClient } from "../lib/api-client";
import { readConfig, writeConfig } from "../lib/config";
import { clearToken, getCredentials, setCredentials } from "../lib/auth";
import { outputOpts } from "../lib/opts";
import {
  success,
  info,
  warn,
  error,
  printFormatted,
} from "../lib/output";
import { describeError, str, unwrap } from "../lib/util";

interface DeviceCodeResponse {
  deviceCode: string;
  userCode: string;
  verificationUri: string;
  verificationUriComplete: string;
  intervalSeconds: number;
  expiresInSeconds: number;
}

interface TokenResponse {
  accessToken: string;
  expiresAt?: string;
  refreshToken?: string;
  sessionId?: string;
  sessionExpiresAt?: string;
}

/** How this CLI appears in Settings → CLI sessions. FMZ_CLIENT_NAME overrides the host name. */
export function clientName(): string {
  const override = process.env.FMZ_CLIENT_NAME;
  if (override && override.trim().length > 0) {
    return override.trim().slice(0, 100);
  }
  try {
    return ("fmz on " + os.hostname()).slice(0, 100);
  } catch {
    return "fmz CLI";
  }
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function login(): Promise<void> {
  const client = await createClient();
  const start = await client.post<DeviceCodeResponse>(
    "/api/v1/cli/auth/device",
    { clientName: clientName() }
  );
  const dc = unwrap(start.data) as DeviceCodeResponse;

  info("To authenticate, visit:");
  info("  " + dc.verificationUri);
  info("And enter code: " + dc.userCode);
  try {
    await open(dc.verificationUriComplete);
  } catch {
    // Headless environment — the user can open the URL manually.
  }

  const intervalMs = Math.max(1, dc.intervalSeconds) * 1000;
  const maxAttempts = Math.ceil(dc.expiresInSeconds / Math.max(1, dc.intervalSeconds));
  info("Waiting for authorization...");

  for (let attempt = 0; attempt < maxAttempts; attempt++) {
    await sleep(intervalMs);
    try {
      const resp = await client.get<TokenResponse>(
        "/api/v1/cli/auth/token",
        {
          params: { deviceCode: dc.deviceCode },
          validateStatus: (s) => s === 200 || s === 202,
        }
      );
      if (resp.status === 202) {
        continue;
      }
      const tok = unwrap(resp.data) as TokenResponse;
      await setCredentials({
        token: tok.accessToken,
        refreshToken: tok.refreshToken,
        expiresAt: tok.expiresAt,
        sessionId: tok.sessionId,
      });
      writeConfig({ token_expiry: tok.expiresAt });
      success("Logged in to Flowamaz.");
      if (tok.refreshToken) {
        info(
          "This terminal stays signed in while you use it. See or end CLI sessions in Settings → CLI sessions, or run: fmz auth logout"
        );
      }
      return;
    } catch (err) {
      error(describeError(err));
      process.exitCode = 1;
      return;
    }
  }
  error("Authorization timed out. Run: fmz auth login");
  process.exitCode = 1;
}

/**
 * Ends this CLI's session on the server (so the token can't be used again), then forgets it locally. If the server
 * can't be reached the local sign-in is still removed, and the person is told how to end the session themselves.
 */
export async function logout(): Promise<void> {
  const creds = await getCredentials();
  if (!creds) {
    writeConfig({ token_expiry: undefined });
    success("Already logged out.");
    return;
  }

  let serverEnded = false;
  let serverError: string | null = null;
  try {
    await axios.post(
      readConfig().api_url + "/api/v1/cli/auth/logout",
      { refreshToken: creds.refreshToken },
      {
        headers: { "Content-Type": "application/json", Authorization: "Bearer " + creds.token },
        // 401 just means the access token had already expired; the refresh token in the body still ends the session.
        validateStatus: (s) => s === 200 || s === 401,
      }
    );
    serverEnded = true;
  } catch (err) {
    serverError = describeError(err);
  }

  await clearToken();
  writeConfig({ token_expiry: undefined });

  if (serverEnded) {
    success("Logged out. This CLI session has been ended on the server.");
  } else {
    success("Logged out on this machine.");
    warn(
      "Couldn't reach Flowamaz to end the session on the server (" +
        serverError +
        "). To be sure it can't be used again, end it in Settings → CLI sessions."
    );
  }
}

async function whoami(cmd: Command): Promise<void> {
  const opts = outputOpts(cmd);
  const config = readConfig();
  try {
    const client = await createClient();
    const resp = await client.get("/api/v1/auth/me");
    printFormatted(unwrap(resp.data), opts);
  } catch {
    // Defensive: endpoint may be absent — show stored context instead.
    printFormatted(
      {
        org: str(config.current_org),
        workspace: str(config.current_workspace),
        token_expiry: str(config.token_expiry),
        session: str((await getCredentials())?.sessionId),
        api_url: config.api_url,
      },
      opts
    );
  }
}

export function registerAuthCommands(program: Command): void {
  const auth = program.command("auth").description("Authentication");

  auth
    .command("login")
    .description("Authenticate via device-code flow")
    .action(async () => {
      try {
        await login();
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });

  auth
    .command("logout")
    .description("End this CLI session on the server and clear stored credentials")
    .action(async () => {
      await logout();
    });

  auth
    .command("whoami")
    .description("Show the current identity")
    .action(async (_opts, cmd: Command) => {
      await whoami(cmd);
    });
}
