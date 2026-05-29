import fs from "fs";
import { Command } from "commander";
import { createClient } from "../lib/api-client";
import { outputOpts } from "../lib/opts";
import { printFormatted, printTable, success, error } from "../lib/output";
import { asArray, describeError, requireWorkspace, str, unwrap } from "../lib/util";

function wfPath(ws: string, id: string): string {
  return (
    "/api/v1/workspaces/" +
    encodeURIComponent(ws) +
    "/workflows/" +
    encodeURIComponent(id)
  );
}

async function history(id: string, cmd: Command): Promise<void> {
  const opts = outputOpts(cmd);
  const ws = requireWorkspace();
  const client = await createClient();
  const resp = await client.get(wfPath(ws, id) + "/history");
  if (opts.json || opts.yaml) {
    printFormatted(resp.data, opts);
    return;
  }
  const rows = asArray(resp.data).map((c) => [
    str(c.sha ?? c.commit),
    str(c.author),
    str(c.date ?? c.timestamp),
    str(c.message),
  ]);
  printTable(["SHA", "Author", "Date", "Message"], rows);
}

async function diff(
  id: string,
  fromSha: string,
  toSha: string | undefined,
  cmd: Command
): Promise<void> {
  const opts = outputOpts(cmd);
  const ws = requireWorkspace();
  const client = await createClient();
  const resp = await client.get(wfPath(ws, id) + "/diff", {
    params: { from: fromSha, to: toSha },
  });
  printFormatted(unwrap(resp.data), opts);
}

async function checkout(
  id: string,
  sha: string,
  cmd: Command,
  outFile?: string
): Promise<void> {
  const opts = outputOpts(cmd);
  const ws = requireWorkspace();
  const client = await createClient();
  const resp = await client.get(wfPath(ws, id) + "/at/" + encodeURIComponent(sha));
  const body = unwrap(resp.data);
  if (outFile) {
    const content =
      typeof body === "object" &&
      body !== null &&
      typeof (body as Record<string, unknown>).yamlContent === "string"
        ? str((body as Record<string, unknown>).yamlContent)
        : str(body);
    fs.writeFileSync(outFile, content, "utf8");
    success("Wrote workflow at " + sha + " to " + outFile);
    return;
  }
  printFormatted(body, opts);
}

export function registerVersionCommands(program: Command): void {
  const version = program
    .command("version")
    .description("Workflow version history (Git-native)");

  version
    .command("history <workflowId>")
    .description("Show commit history for a workflow")
    .action(async (id: string, _o, cmd: Command) => {
      try {
        await history(id, cmd);
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });

  version
    .command("diff <workflowId> <fromSha> [toSha]")
    .description("Diff a workflow between two revisions")
    .action(
      async (
        id: string,
        fromSha: string,
        toSha: string | undefined,
        _o: unknown,
        cmd: Command
      ) => {
        try {
          await diff(id, fromSha, toSha, cmd);
        } catch (err) {
          error(describeError(err));
          process.exitCode = 1;
        }
      }
    );

  version
    .command("checkout <workflowId> <sha>")
    .description("Fetch a workflow at a specific revision")
    .option("--out <file>", "Write YAML to a file instead of stdout")
    .action(
      async (
        id: string,
        sha: string,
        options: { out?: string },
        cmd: Command
      ) => {
        try {
          await checkout(id, sha, cmd, options.out);
        } catch (err) {
          error(describeError(err));
          process.exitCode = 1;
        }
      }
    );
}
