import fs from "fs";
import { Command } from "commander";
import { AxiosInstance } from "axios";
import chalk from "chalk";
import { createClient } from "../lib/api-client";
import { outputOpts } from "../lib/opts";
import { printFormatted, printTable, success, error } from "../lib/output";
import { asArray, describeError, requireWorkspace, str, unwrap } from "../lib/util";

export interface ValidationIssue {
  message: string;
  line?: number;
}

export interface ValidationResponse {
  errors: ValidationIssue[];
  warnings: ValidationIssue[];
}

export interface ValidationResult {
  output: string;
  exitCode: number;
}

/**
 * Render a validation response and decide the exit code. Pure & testable:
 * no network, no process side effects.
 */
export function renderValidation(resp: ValidationResponse): ValidationResult {
  const lines: string[] = [];
  const errors = resp.errors ?? [];
  const warnings = resp.warnings ?? [];

  for (const e of errors) {
    const loc = e.line !== undefined ? " (line " + e.line + ")" : "";
    lines.push(chalk.red("error" + loc + ": " + e.message));
  }
  for (const w of warnings) {
    const loc = w.line !== undefined ? " (line " + w.line + ")" : "";
    lines.push(chalk.yellow("warning" + loc + ": " + w.message));
  }
  if (errors.length === 0 && warnings.length === 0) {
    lines.push(chalk.green("✓ Workflow is valid."));
  } else {
    lines.push(
      "\n" +
        errors.length +
        " error(s), " +
        warnings.length +
        " warning(s)."
    );
  }
  return {
    output: lines.join("\n"),
    exitCode: errors.length > 0 ? 1 : 0,
  };
}

/**
 * Core validate action: load file, call the API, render. Exported for tests
 * (inject a client). Returns the exit code rather than calling process.exit.
 */
export async function runValidate(
  client: AxiosInstance,
  workspace: string,
  filePath: string,
  write: (line: string) => void
): Promise<number> {
  const yamlContent = fs.readFileSync(filePath, "utf8");
  const resp = await client.post(
    "/api/v1/workspaces/" + encodeURIComponent(workspace) + "/workflows/validate",
    { yamlContent }
  );
  const body = unwrap(resp.data) as ValidationResponse;
  const result = renderValidation({
    errors: body.errors ?? [],
    warnings: body.warnings ?? [],
  });
  write(result.output);
  return result.exitCode;
}

async function list(cmd: Command): Promise<void> {
  const opts = outputOpts(cmd);
  const ws = requireWorkspace();
  const client = await createClient();
  const resp = await client.get(
    "/api/v1/workspaces/" + encodeURIComponent(ws) + "/workflows"
  );
  if (opts.json || opts.yaml) {
    printFormatted(resp.data, opts);
    return;
  }
  const rows = asArray(resp.data).map((w) => [
    str(w.id),
    str(w.name),
    str(w.status),
    str(w.version),
  ]);
  printTable(["ID", "Name", "Status", "Version"], rows);
}

async function get(id: string, cmd: Command): Promise<void> {
  const opts = outputOpts(cmd);
  const ws = requireWorkspace();
  const client = await createClient();
  const resp = await client.get(
    "/api/v1/workspaces/" + encodeURIComponent(ws) + "/workflows/" + encodeURIComponent(id)
  );
  printFormatted(unwrap(resp.data), opts);
}

async function create(file: string, cmd: Command): Promise<void> {
  const opts = outputOpts(cmd);
  const ws = requireWorkspace();
  const yamlContent = fs.readFileSync(file, "utf8");
  const client = await createClient();
  const resp = await client.post(
    "/api/v1/workspaces/" + encodeURIComponent(ws) + "/workflows",
    { yamlContent }
  );
  printFormatted(unwrap(resp.data), opts);
  success("Workflow created.");
}

async function update(id: string, file: string, cmd: Command): Promise<void> {
  const opts = outputOpts(cmd);
  const ws = requireWorkspace();
  const yamlContent = fs.readFileSync(file, "utf8");
  const client = await createClient();
  const resp = await client.put(
    "/api/v1/workspaces/" + encodeURIComponent(ws) + "/workflows/" + encodeURIComponent(id),
    { yamlContent }
  );
  printFormatted(unwrap(resp.data), opts);
  success("Workflow updated.");
}

async function publish(id: string, cmd: Command, environment?: string): Promise<void> {
  const opts = outputOpts(cmd);
  const ws = requireWorkspace();
  const client = await createClient();
  const resp = await client.post(
    "/api/v1/workspaces/" + encodeURIComponent(ws) + "/workflows/" + encodeURIComponent(id) + "/publish",
    environment ? { environment } : {}
  );
  printFormatted(unwrap(resp.data), opts);
  success("Workflow published" + (environment ? " to " + environment : "") + ".");
}

async function deploy(
  id: string,
  cmd: Command,
  environment: string,
  test: boolean
): Promise<void> {
  const ws = requireWorkspace();
  const client = await createClient();
  await client.post(
    "/api/v1/workspaces/" + encodeURIComponent(ws) + "/workflows/" + encodeURIComponent(id) + "/publish",
    { environment }
  );
  success("Published to " + environment + ".");
  if (test) {
    const resp = await client.post(
      "/api/v1/workspaces/" + encodeURIComponent(ws) + "/instances",
      { workflowDefinitionId: id, payload: {} }
    );
    const inst = unwrap(resp.data) as Record<string, unknown>;
    success("Test instance triggered: " + str(inst.id));
  }
}

export function registerWorkflowCommands(program: Command): void {
  const wf = program.command("workflow").description("Manage workflows");

  const wrap =
    (fn: (cmd: Command) => Promise<void>) =>
    async (_o: unknown, cmd: Command): Promise<void> => {
      try {
        await fn(cmd);
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    };

  wf.command("list").description("List workflows").action(wrap(list));

  wf.command("get <id>")
    .description("Show a workflow")
    .action(async (id: string, _o, cmd: Command) => {
      try {
        await get(id, cmd);
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });

  wf.command("create <file>")
    .description("Create a workflow from a YAML file")
    .action(async (file: string, _o, cmd: Command) => {
      try {
        await create(file, cmd);
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });

  wf.command("update <id> <file>")
    .description("Update a workflow from a YAML file")
    .action(async (id: string, file: string, _o, cmd: Command) => {
      try {
        await update(id, file, cmd);
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });

  wf.command("validate <file>")
    .description("Validate a workflow YAML file via the API")
    .action(async (file: string, _o, cmd: Command) => {
      try {
        const ws = requireWorkspace();
        const client = await createClient();
        const code = await runValidate(client, ws, file, (line) =>
          process.stdout.write(line + "\n")
        );
        process.exitCode = code;
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });

  wf.command("publish <id>")
    .description("Publish a workflow")
    .option("--environment <env>", "Target environment")
    .action(async (id: string, options: { environment?: string }, cmd: Command) => {
      try {
        await publish(id, cmd, options.environment);
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });

  wf.command("deploy <id>")
    .description("Publish then optionally trigger a test instance")
    .requiredOption("--environment <env>", "Target environment")
    .option("--test", "Trigger a test instance after publish", false)
    .action(
      async (
        id: string,
        options: { environment: string; test?: boolean },
        cmd: Command
      ) => {
        try {
          await deploy(id, cmd, options.environment, options.test === true);
        } catch (err) {
          error(describeError(err));
          process.exitCode = 1;
        }
      }
    );
}
