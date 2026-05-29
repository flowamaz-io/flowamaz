import { Command } from "commander";
import { AxiosInstance } from "axios";
import { createClient } from "../lib/api-client";
import { outputOpts } from "../lib/opts";
import { printFormatted, info, success, error } from "../lib/output";
import { describeError, requireWorkspace, str, unwrap } from "../lib/util";

const TERMINAL_STATES = new Set([
  "completed",
  "failed",
  "cancelled",
  "canceled",
  "succeeded",
  "errored",
]);

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function parsePayload(raw?: string): unknown {
  if (!raw) {
    return {};
  }
  try {
    return JSON.parse(raw) as unknown;
  } catch {
    throw new Error("--payload must be valid JSON.");
  }
}

async function getStatus(
  client: AxiosInstance,
  ws: string,
  id: string
): Promise<Record<string, unknown>> {
  const resp = await client.get(
    "/api/v1/workspaces/" + encodeURIComponent(ws) + "/instances/" + encodeURIComponent(id)
  );
  return unwrap(resp.data) as Record<string, unknown>;
}

async function trigger(
  workflowId: string,
  cmd: Command,
  payloadRaw?: string,
  wait?: boolean
): Promise<void> {
  const opts = outputOpts(cmd);
  const ws = requireWorkspace();
  const client = await createClient();
  const payload = parsePayload(payloadRaw);
  const resp = await client.post(
    "/api/v1/workspaces/" + encodeURIComponent(ws) + "/instances",
    { workflowDefinitionId: workflowId, payload }
  );
  const inst = unwrap(resp.data) as Record<string, unknown>;
  const id = str(inst.id);
  success("Instance triggered: " + id);

  if (wait && id) {
    info("Waiting for completion...");
    for (let i = 0; i < 600; i++) {
      await sleep(2000);
      const status = await getStatus(client, ws, id);
      const state = str(status.status ?? status.state).toLowerCase();
      if (TERMINAL_STATES.has(state)) {
        printFormatted(status, opts);
        return;
      }
    }
    error("Timed out waiting for instance to complete.");
    process.exitCode = 1;
    return;
  }
  printFormatted(inst, opts);
}

async function status(id: string, cmd: Command): Promise<void> {
  const opts = outputOpts(cmd);
  const ws = requireWorkspace();
  const client = await createClient();
  printFormatted(await getStatus(client, ws, id), opts);
}

async function logs(id: string, cmd: Command, follow: boolean): Promise<void> {
  const opts = outputOpts(cmd);
  const ws = requireWorkspace();
  const client = await createClient();
  const path =
    "/api/v1/workspaces/" + encodeURIComponent(ws) + "/instances/" + encodeURIComponent(id) + "/events";

  if (!follow) {
    const resp = await client.get(path);
    printFormatted(unwrap(resp.data), opts);
    return;
  }

  let seen = 0;
  for (let i = 0; i < 1800; i++) {
    const resp = await client.get(path);
    const events = unwrap(resp.data);
    if (Array.isArray(events)) {
      for (let j = seen; j < events.length; j++) {
        printFormatted(events[j], opts);
      }
      seen = events.length;
    }
    const inst = await getStatus(client, ws, id);
    const state = str(inst.status ?? inst.state).toLowerCase();
    if (TERMINAL_STATES.has(state)) {
      return;
    }
    await sleep(2000);
  }
}

async function cancel(id: string): Promise<void> {
  const ws = requireWorkspace();
  const client = await createClient();
  await client.post(
    "/api/v1/workspaces/" + encodeURIComponent(ws) + "/instances/" + encodeURIComponent(id) + "/cancel"
  );
  success("Instance cancelled: " + id);
}

async function retry(id: string, cmd: Command): Promise<void> {
  const opts = outputOpts(cmd);
  const ws = requireWorkspace();
  const client = await createClient();
  const resp = await client.post(
    "/api/v1/workspaces/" + encodeURIComponent(ws) + "/instances/" + encodeURIComponent(id) + "/retry"
  );
  printFormatted(unwrap(resp.data), opts);
  success("Instance retried.");
}

export function registerInstanceCommands(program: Command): void {
  const inst = program.command("instance").description("Manage workflow instances");

  inst
    .command("trigger <workflowId>")
    .description("Trigger a workflow instance")
    .option("--payload <json>", "JSON payload")
    .option("--wait", "Wait until the instance reaches a terminal state", false)
    .action(
      async (
        workflowId: string,
        options: { payload?: string; wait?: boolean },
        cmd: Command
      ) => {
        try {
          await trigger(workflowId, cmd, options.payload, options.wait === true);
        } catch (err) {
          error(describeError(err));
          process.exitCode = 1;
        }
      }
    );

  inst
    .command("status <id>")
    .description("Show instance status")
    .action(async (id: string, _o, cmd: Command) => {
      try {
        await status(id, cmd);
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });

  inst
    .command("logs <id>")
    .description("Show instance logs/events")
    .option("--follow", "Poll for new events every 2s", false)
    .action(async (id: string, options: { follow?: boolean }, cmd: Command) => {
      try {
        await logs(id, cmd, options.follow === true);
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });

  inst
    .command("cancel <id>")
    .description("Cancel a running instance")
    .action(async (id: string) => {
      try {
        await cancel(id);
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });

  inst
    .command("retry <id>")
    .description("Retry a failed instance")
    .action(async (id: string, _o, cmd: Command) => {
      try {
        await retry(id, cmd);
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });
}
