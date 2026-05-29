import { Command } from "commander";
import open from "open";
import { createClient } from "../lib/api-client";
import { readConfig, writeConfig } from "../lib/config";
import { setToken, clearToken } from "../lib/auth";
import { outputOpts } from "../lib/opts";
import {
  success,
  info,
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
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function login(): Promise<void> {
  const client = await createClient();
  const start = await client.post<DeviceCodeResponse>(
    "/api/v1/cli/auth/device"
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
      await setToken(tok.accessToken);
      if (tok.expiresAt) {
        writeConfig({ token_expiry: tok.expiresAt });
      }
      success("Logged in to Flowamaz.");
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

async function logout(): Promise<void> {
  await clearToken();
  writeConfig({ token_expiry: undefined });
  success("Logged out.");
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
    .description("Clear stored credentials")
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
