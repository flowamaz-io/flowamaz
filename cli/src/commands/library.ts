import fs from "fs";
import path from "path";
import { Command } from "commander";
import yaml from "js-yaml";
import { createClient } from "../lib/api-client";
import { outputOpts } from "../lib/opts";
import { printFormatted, printTable, success, error } from "../lib/output";
import { asArray, describeError, str, unwrap } from "../lib/util";

// ---- Marketplace (connectors + templates) ----

async function list(kind: string, cmd: Command): Promise<void> {
  const opts = outputOpts(cmd);
  const client = await createClient();
  const resp = await client.get("/api/v1/library/" + kind);
  if (opts.json || opts.yaml) {
    printFormatted(resp.data, opts);
    return;
  }
  const rows = asArray(resp.data).map((i) => [
    str(i.id ?? i.slug),
    str(i.name),
    str(i.version),
    str(i.publisher ?? i.author),
  ]);
  printTable(["ID", "Name", "Version", "Publisher"], rows);
}

async function search(kind: string, query: string, cmd: Command): Promise<void> {
  const opts = outputOpts(cmd);
  const client = await createClient();
  const resp = await client.get("/api/v1/library/" + kind, {
    params: { q: query },
  });
  if (opts.json || opts.yaml) {
    printFormatted(resp.data, opts);
    return;
  }
  const rows = asArray(resp.data).map((i) => [
    str(i.id ?? i.slug),
    str(i.name),
    str(i.description),
  ]);
  printTable(["ID", "Name", "Description"], rows);
}

async function show(kind: string, id: string, cmd: Command): Promise<void> {
  const opts = outputOpts(cmd);
  const client = await createClient();
  const resp = await client.get(
    "/api/v1/library/" + kind + "/" + encodeURIComponent(id)
  );
  printFormatted(unwrap(resp.data), opts);
}

async function install(kind: string, id: string): Promise<void> {
  const client = await createClient();
  await client.post(
    "/api/v1/library/" + kind + "/" + encodeURIComponent(id) + "/install"
  );
  success("Installed " + kind.replace(/s$/, "") + ": " + id);
}

async function outdated(kind: string, cmd: Command): Promise<void> {
  const opts = outputOpts(cmd);
  const client = await createClient();
  const resp = await client.get("/api/v1/library/" + kind + "/outdated");
  if (opts.json || opts.yaml) {
    printFormatted(resp.data, opts);
    return;
  }
  const rows = asArray(resp.data).map((i) => [
    str(i.id ?? i.slug),
    str(i.installedVersion ?? i.current),
    str(i.latestVersion ?? i.latest),
  ]);
  printTable(["ID", "Installed", "Latest"], rows);
}

async function update(kind: string, id: string): Promise<void> {
  const client = await createClient();
  await client.post(
    "/api/v1/library/" + kind + "/" + encodeURIComponent(id) + "/update"
  );
  success("Updated " + kind.replace(/s$/, "") + ": " + id);
}

// ---- Authoring: init / validate / publish ----

function scaffoldInit(kind: "connector" | "template", name: string): void {
  const dir = path.resolve(process.cwd(), name);
  if (fs.existsSync(dir)) {
    throw new Error("Directory already exists: " + dir);
  }
  fs.mkdirSync(dir, { recursive: true });
  const manifest =
    kind === "connector"
      ? {
          kind: "connector",
          name,
          version: "0.1.0",
          description: "A Flowamaz connector.",
          actions: [],
        }
      : {
          kind: "template",
          name,
          version: "0.1.0",
          description: "A Flowamaz workflow template.",
          workflow: {},
        };
  fs.writeFileSync(
    path.join(dir, kind + ".yaml"),
    yaml.dump(manifest),
    "utf8"
  );
  success("Scaffolded " + kind + " at " + dir);
}

function readManifest(kind: "connector" | "template", file?: string): {
  filePath: string;
  content: string;
} {
  const filePath = file ?? path.resolve(process.cwd(), kind + ".yaml");
  if (!fs.existsSync(filePath)) {
    throw new Error("Manifest not found: " + filePath);
  }
  return { filePath, content: fs.readFileSync(filePath, "utf8") };
}

async function publishAuthored(
  kind: "connector" | "template",
  file: string | undefined,
  cmd: Command
): Promise<void> {
  const opts = outputOpts(cmd);
  const { content } = readManifest(kind, file);
  const client = await createClient();
  const resp = await client.post("/api/v1/library/" + kind + "s/publish", {
    manifest: content,
  });
  printFormatted(unwrap(resp.data), opts);
  success(kind + " published.");
}

function validateAuthored(kind: "connector" | "template", file?: string): number {
  const { content } = readManifest(kind, file);
  let parsed: unknown;
  try {
    parsed = yaml.load(content);
  } catch (err) {
    error("Invalid YAML: " + describeError(err));
    return 1;
  }
  if (!parsed || typeof parsed !== "object") {
    error("Manifest must be a YAML object.");
    return 1;
  }
  const obj = parsed as Record<string, unknown>;
  const missing: string[] = [];
  if (typeof obj.name !== "string") missing.push("name");
  if (typeof obj.version !== "string") missing.push("version");
  if (missing.length > 0) {
    error("Missing required field(s): " + missing.join(", "));
    return 1;
  }
  success(kind + " manifest is valid.");
  return 0;
}

function registerAuthoringGroup(
  parent: Command,
  kind: "connector" | "template"
): void {
  const group = parent
    .command(kind)
    .description("Author and publish a " + kind);

  group
    .command("init <name>")
    .description("Scaffold a new " + kind)
    .action((name: string) => {
      try {
        scaffoldInit(kind, name);
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });

  group
    .command("validate [file]")
    .description("Validate a " + kind + " manifest")
    .action((file: string | undefined) => {
      try {
        process.exitCode = validateAuthored(kind, file);
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });

  group
    .command("publish [file]")
    .description("Publish a " + kind + " to the Library")
    .action(async (file: string | undefined, _o, cmd: Command) => {
      try {
        await publishAuthored(kind, file, cmd);
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });
}

function wrapKind(
  fn: (kind: string, cmd: Command) => Promise<void>,
  kind: string
) {
  return async (_o: unknown, cmd: Command): Promise<void> => {
    try {
      await fn(kind, cmd);
    } catch (err) {
      error(describeError(err));
      process.exitCode = 1;
    }
  };
}

export function registerLibraryCommands(program: Command): void {
  const lib = program
    .command("library")
    .description("Connector & template Library");

  lib
    .command("list")
    .description("List installed/available items")
    .option("--connectors", "List connectors", false)
    .option("--templates", "List templates", false)
    .action(
      async (
        options: { connectors?: boolean; templates?: boolean },
        cmd: Command
      ) => {
        const kind = options.templates ? "templates" : "connectors";
        await wrapKind(list, kind)(undefined, cmd);
      }
    );

  lib
    .command("search <query>")
    .description("Search the Library")
    .option("--templates", "Search templates instead of connectors", false)
    .action(
      async (query: string, options: { templates?: boolean }, cmd: Command) => {
        const kind = options.templates ? "templates" : "connectors";
        try {
          await search(kind, query, cmd);
        } catch (err) {
          error(describeError(err));
          process.exitCode = 1;
        }
      }
    );

  lib
    .command("show <id>")
    .description("Show Library item details")
    .option("--templates", "Treat id as a template", false)
    .action(
      async (id: string, options: { templates?: boolean }, cmd: Command) => {
        const kind = options.templates ? "templates" : "connectors";
        try {
          await show(kind, id, cmd);
        } catch (err) {
          error(describeError(err));
          process.exitCode = 1;
        }
      }
    );

  lib
    .command("install <id>")
    .description("Install a Library item into the workspace")
    .option("--templates", "Install a template", false)
    .action(async (id: string, options: { templates?: boolean }) => {
      const kind = options.templates ? "templates" : "connectors";
      try {
        await install(kind, id);
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });

  lib
    .command("outdated")
    .description("Show installed items with newer versions")
    .option("--templates", "Check templates", false)
    .action(async (options: { templates?: boolean }, cmd: Command) => {
      const kind = options.templates ? "templates" : "connectors";
      await wrapKind(outdated, kind)(undefined, cmd);
    });

  lib
    .command("update <id>")
    .description("Update an installed item")
    .option("--templates", "Update a template", false)
    .action(async (id: string, options: { templates?: boolean }) => {
      const kind = options.templates ? "templates" : "connectors";
      try {
        await update(kind, id);
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });

  registerAuthoringGroup(lib, "connector");
  registerAuthoringGroup(lib, "template");
}
