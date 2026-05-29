import { Command } from "commander";
import { createClient } from "../lib/api-client";
import { writeConfig } from "../lib/config";
import { outputOpts } from "../lib/opts";
import { printFormatted, printTable, success, error } from "../lib/output";
import { asArray, describeError, str } from "../lib/util";

async function list(cmd: Command): Promise<void> {
  const opts = outputOpts(cmd);
  const client = await createClient();
  const resp = await client.get("/api/v1/workspaces");
  if (opts.json || opts.yaml) {
    printFormatted(resp.data, opts);
    return;
  }
  const rows = asArray(resp.data).map((w) => [
    str(w.id),
    str(w.name),
    str(w.slug),
  ]);
  printTable(["ID", "Name", "Slug"], rows);
}

async function use(slug: string): Promise<void> {
  writeConfig({ current_workspace: slug });
  success("Active workspace set to: " + slug);
}

async function create(
  name: string,
  cmd: Command,
  slugOpt?: string
): Promise<void> {
  const opts = outputOpts(cmd);
  const client = await createClient();
  const slug = slugOpt ?? name.toLowerCase().replace(/[^a-z0-9]+/g, "-");
  const resp = await client.post("/api/v1/workspaces", { name, slug });
  printFormatted(resp.data, opts);
  success("Workspace created: " + slug);
}

async function members(ws: string, cmd: Command): Promise<void> {
  const opts = outputOpts(cmd);
  const client = await createClient();
  const resp = await client.get(
    "/api/v1/workspaces/" + encodeURIComponent(ws) + "/members"
  );
  if (opts.json || opts.yaml) {
    printFormatted(resp.data, opts);
    return;
  }
  const rows = asArray(resp.data).map((m) => [
    str(m.id ?? m.userId),
    str(m.email ?? m.name),
    str(m.role),
  ]);
  printTable(["ID", "Member", "Role"], rows);
}

export function registerWorkspaceCommands(program: Command): void {
  const ws = program.command("workspace").description("Manage workspaces");

  ws.command("list")
    .description("List workspaces")
    .action(async (_o, cmd: Command) => {
      try {
        await list(cmd);
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });

  ws.command("use <slug>")
    .description("Set the active workspace")
    .action(async (slug: string) => {
      await use(slug);
    });

  ws.command("create <name>")
    .description("Create a workspace")
    .option("--slug <slug>", "Workspace slug")
    .action(async (name: string, options: { slug?: string }, cmd: Command) => {
      try {
        await create(name, cmd, options.slug);
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });

  ws.command("members <workspace>")
    .description("List workspace members")
    .action(async (workspace: string, _o, cmd: Command) => {
      try {
        await members(workspace, cmd);
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });
}
