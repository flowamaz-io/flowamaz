import fs from "fs";
import path from "path";
import { Command } from "commander";
import yaml from "js-yaml";
import { createClient } from "../lib/api-client";
import { outputOpts } from "../lib/opts";
import { printFormatted, printTable, success, error } from "../lib/output";
import { asArray, describeError, noOpMessage, requireWorkspaceId, str, unwrap } from "../lib/util";

// ---- Library (connectors, templates, forms, packs), FUNCTIONAL.md §23.2 / §23.5 ----
// Every call is workspace-scoped: /api/v1/workspaces/{id}/library/...

type Kind = "connectors" | "templates" | "forms" | "packs";

interface LibraryOpts {
  workspace?: string;
  templates?: boolean;
  template?: boolean;
  forms?: boolean;
  packs?: boolean;
  type?: string;
  category?: string;
  name?: string;
}

/** --templates / --forms / --packs, or --type template|form|pack; connectors otherwise. */
export function kindOf(o: LibraryOpts): Kind {
  const type = (o.type ?? "").toLowerCase();
  if (o.packs || type.startsWith("pack")) return "packs";
  if (o.forms || type.startsWith("form")) return "forms";
  if (o.templates || o.template || type.startsWith("template")) return "templates";
  return "connectors";
}

async function base(o: LibraryOpts): Promise<string> {
  const ws = await requireWorkspaceId(o.workspace);
  return "/api/v1/workspaces/" + encodeURIComponent(ws) + "/library/";
}

function itemRow(kind: Kind, i: Record<string, unknown>): string[] {
  if (kind === "forms") {
    const fields = Array.isArray(i.fields) ? (i.fields as Record<string, unknown>[]) : [];
    const owner = i.isWorkspaceForm ? "this workspace" : str(i.publisher ?? "");
    return [str(i.slug), str(i.name), String(fields.length), owner, fields.map((f) => str(f.key)).join(", ")];
  }
  if (kind === "packs") {
    const items = Array.isArray(i.items) ? (i.items as Record<string, unknown>[]) : [];
    const count = (k: string): number => items.filter((x) => x.kind === k).length;
    const missing = items.filter((x) => x.available && !x.installed).length;
    return [str(i.slug), str(i.name), count("connector") + " / " + count("template") + " / " + count("form"),
      str(i.publisher ?? ""), missing === 0 ? "everything installed" : missing + " to install"];
  }
  if (kind === "templates") {
    return [str(i.slug), str(i.name), str(i.version), str(i.publisher ?? (i.isOfficial ? "flowamaz-io" : "")), str(i.category)];
  }
  const installed = i.installed ? "installed " + str(i.installedVersion) + " (" + str(i.installedPin) + ")" : "";
  const deprecated = i.deprecatedAt ? "deprecated, leaves " + str(i.removalAt).slice(0, 10) : "";
  return [str(i.id), str(i.name), str(i.version), str(i.publisher), [installed, deprecated].filter(Boolean).join("; ")];
}

const HEADERS: Record<Kind, string[]> = {
  connectors: ["ID", "Name", "Version", "Publisher", "This workspace"],
  templates: ["Slug", "Name", "Version", "Publisher", "Category"],
  forms: ["Slug", "Name", "Fields", "From", "Keys"],
  packs: ["Slug", "Name", "Connectors / templates / forms", "Publisher", "This workspace"],
};

async function list(o: LibraryOpts, cmd: Command, query?: string): Promise<void> {
  const opts = outputOpts(cmd);
  const kind = kindOf(o);
  const client = await createClient();
  const resp = await client.get((await base(o)) + kind, { params: { q: query, category: o.category } });
  if (opts.json || opts.yaml) {
    printFormatted(unwrap(resp.data), opts);
    return;
  }
  const rows = asArray(resp.data).map((i) => itemRow(kind, i));
  if (rows.length === 0) {
    success(query ? "Nothing in the Library matches \"" + query + "\". Try fewer or different words." : "The Library has no " + kind + " yet.");
    return;
  }
  printTable(HEADERS[kind], rows);
}

async function show(o: LibraryOpts, id: string, cmd: Command): Promise<void> {
  const opts = outputOpts(cmd);
  const client = await createClient();
  const resp = await client.get((await base(o)) + kindOf(o) + "/" + id.split("/").map(encodeURIComponent).join("/"));
  printFormatted(unwrap(resp.data), opts);
}

async function install(o: LibraryOpts, id: string): Promise<void> {
  const kind = kindOf(o);
  if (kind === "forms") {
    throw new Error(
      "Forms aren't installed: a human gate copies one in. Open the workflow, select the gate and choose \"Use a Library form\", or put its fields under config.form in the YAML (fmz library show " + id + " --forms)."
    );
  }
  const client = await createClient();
  if (kind === "packs") {
    const result = unwrap((await client.post((await base(o)) + "packs/install", { id })).data) as Record<string, unknown>;
    const items = Array.isArray(result?.items) ? (result.items as Record<string, unknown>[]) : [];
    printTable(["Kind", "Item", "Result"], items.map((r) => [str(r.kind), str(r.name), str(r.status).replace("_", " ") + ": " + str(r.message)]));
    const failed = Number(result?.failed ?? 0);
    success("Pack " + str(result?.pack ?? id) + ": " + str(result?.installed) + " installed, " + str(result?.alreadyInstalled) + " already here" +
      (failed > 0 ? ", " + failed + " not installed (see above)." : "."));
    if (failed > 0) process.exitCode = 1;
    return;
  }
  let resp;
  try {
    resp = await client.post((await base(o)) + kind + "/install", { id, name: o.name });
  } catch (err) {
    // Re-running an install is fine: say it's already there and exit 0.
    const already = noOpMessage(err, ["LIBRARY_CONNECTOR_INSTALLED"]);
    if (already === null) throw err;
    success(already);
    return;
  }
  const data = unwrap(resp.data) as Record<string, unknown>;
  if (kind === "templates") {
    success("Installed template " + id + " as a new workflow: " + str(data?.workflowId));
  } else {
    success("Installed " + str(data?.name ?? id) + " " + str(data?.installedVersion) + " (" + str(data?.installedPin) + ")");
  }
}

async function outdated(o: LibraryOpts, cmd: Command): Promise<void> {
  const opts = outputOpts(cmd);
  if (kindOf(o) === "forms" || kindOf(o) === "packs") {
    throw new Error("Only connectors update in place. Forms are copied onto gates and packs install copies, so there's nothing outdated to list. Run: fmz library outdated");
  }
  const client = await createClient();
  const resp = await client.get((await base(o)) + kindOf(o) + "/outdated");
  if (opts.json || opts.yaml) {
    printFormatted(unwrap(resp.data), opts);
    return;
  }
  const rows = asArray(resp.data).map((i) => [
    str(i.id),
    str(i.installedVersion) + " (" + str(i.installedPin) + ")",
    str(i.latestVersion ?? "—"),
    i.deprecatedAt ? "deprecated: " + str(i.deprecationNote) + " Leaves the Library " + str(i.removalAt).slice(0, 10) + "." : "new major: run fmz library update " + str(i.id),
  ]);
  if (rows.length === 0) {
    success("Everything installed is on its latest major. Minor and patch releases are applied automatically.");
    return;
  }
  printTable(["ID", "Installed", "Latest", "What to do"], rows);
}

async function update(o: LibraryOpts, id: string): Promise<void> {
  if (kindOf(o) === "forms" || kindOf(o) === "packs") {
    throw new Error("Only connectors update in place. To get a newer pack's workflows, run: fmz library install " + id + " --packs (it adds what's missing).");
  }
  if (kindOf(o) === "templates") {
    throw new Error(
      "Templates are copied into your workspace when you install them, so they don't update in place. Install the new version again: fmz library install " + id + " --template"
    );
  }
  const client = await createClient();
  let resp;
  try {
    resp = await client.post((await base(o)) + "connectors/update", { id });
  } catch (err) {
    const upToDate = noOpMessage(err, ["LIBRARY_CONNECTOR_UP_TO_DATE"]);
    if (upToDate === null) throw err;
    success(upToDate);
    return;
  }
  const data = unwrap(resp.data) as Record<string, unknown>;
  success("Upgraded " + id + " from " + str(data?.fromVersion) + " to " + str(data?.toVersion) + " (" + str(data?.pin) + ")");
}

function libraryAction<A extends unknown[]>(fn: (...args: A) => Promise<void>) {
  return async (...args: A): Promise<void> => {
    try {
      await fn(...args);
    } catch (err) {
      error(describeError(err));
      process.exitCode = 1;
    }
  };
}

// ---- Authoring: init / validate / publish ----

function scaffoldInit(kind: "connector" | "template", name: string): void {
  const dir = path.resolve(process.cwd(), name);
  if (fs.existsSync(dir)) {
    throw new Error("Directory already exists: " + dir);
  }
  fs.mkdirSync(dir, { recursive: true });
  // The layout the Library sync reads (FUNCTIONAL.md §23.3-23.4). The publisher is your organisation,
  // set when you publish.
  const manifest =
    kind === "connector"
      ? {
          id: name,
          version: "0.1.0",
          display_name: name,
          category: "generic",
          description: "A Flowamaz connector.",
          auth: { type: "api_key" },
          network: { allowed_domains: ["api.example.com"] },
          operations: [],
        }
      : {
          name,
          version: "0.1.0",
          category: "Custom",
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
  cmd: Command,
  workspace?: string
): Promise<void> {
  const opts = outputOpts(cmd);
  const { content } = readManifest(kind, file);
  const client = await createClient();
  const resp = await client.post((await base({ workspace })) + kind + "s/publish", {
    manifest: content,
  });
  const data = unwrap(resp.data) as Record<string, unknown>;
  if (opts.json || opts.yaml) {
    printFormatted(data, opts);
    return;
  }
  success(str(data?.message ?? kind + " submitted."));
  if (data?.pullRequestUrl) success("Pull request: " + str(data.pullRequestUrl));
}

const SLUG = /^[a-z0-9][a-z0-9-]{0,63}$/;
const SEMVER = /^v?\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$/;

/** The checks the Library sync applies, as messages that say what to fix. Empty = valid. */
export function manifestProblems(kind: "connector" | "template", m: Record<string, unknown>): string[] {
  const problems: string[] = [];
  const version = m.version;
  if (typeof version !== "string" || !SEMVER.test(version)) {
    problems.push("version must be semver, e.g. 1.0.0 (quote it if YAML reads it as a number).");
  }
  if (kind === "connector") {
    const id = m.id ?? m.name;
    if (typeof id !== "string" || !SLUG.test(id)) {
      problems.push("id must be lowercase letters, numbers and hyphens, e.g. my-connector.");
    }
    if (m.tier === "official" || m.official === true) {
      problems.push("Only flowamaz-io publishes official connectors. Remove tier/official.");
    }
    const domains = (m.network as Record<string, unknown> | undefined)?.allowed_domains;
    if (Array.isArray(domains) && domains.some((d) => typeof d === "string" && d.includes("*"))) {
      problems.push("network.allowed_domains has a wildcard. List each domain; wildcards need a human review.");
    }
  } else {
    if (typeof m.name !== "string" || m.name.trim().length === 0) problems.push("name is required.");
    const wf = m.workflow;
    if (!(typeof m.workflow_yaml === "string" && m.workflow_yaml.trim()) && !(wf && typeof wf === "object" && Object.keys(wf).length > 0)) {
      problems.push("workflow is empty. Put the complete workflow YAML under `workflow:`.");
    }
  }
  return problems;
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
  const problems = manifestProblems(kind, parsed as Record<string, unknown>);
  if (problems.length > 0) {
    for (const p of problems) error(p);
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
    .option("-w, --workspace <workspace>", "Workspace id or slug (default: fmz workspace use)")
    .action(async (file: string | undefined, o: { workspace?: string }, cmd: Command) => {
      try {
        await publishAuthored(kind, file, cmd, o.workspace);
      } catch (err) {
        error(describeError(err));
        process.exitCode = 1;
      }
    });
}

export function registerLibraryCommands(program: Command): void {
  const lib = program
    .command("library")
    .description("Library: connectors, templates, forms and packs");

  const scoped = (c: Command): Command =>
    c
      .option("-w, --workspace <workspace>", "Workspace id or slug (default: fmz workspace use)")
      .option("--templates", "Templates instead of connectors", false)
      .option("--template", "Same as --templates", false)
      .option("--forms", "Human-gate forms instead of connectors", false)
      .option("--packs", "Packs instead of connectors", false);

  scoped(lib.command("list").description("List Library connectors (or --templates)"))
    .option("--type <type>", "connector, template, form or pack")
    .option("--category <category>", "Only this category, e.g. finance")
    .action(libraryAction((o: LibraryOpts, cmd: Command) => list(o, cmd)));

  scoped(lib.command("search <query>").description("Search the Library"))
    .option("--category <category>", "Only this category")
    .action(libraryAction((query: string, o: LibraryOpts, cmd: Command) => list(o, cmd, query)));

  scoped(lib.command("show <id>").description("Show a connector (id or publisher/id), template, form or pack (slug)"))
    .action(libraryAction((id: string, o: LibraryOpts, cmd: Command) => show(o, id, cmd)));

  scoped(lib.command("install <id>").description("Install a connector, a template as a new workflow, or a pack (--packs)"))
    .option("--name <name>", "Name for the workflow a template creates")
    .action(libraryAction((id: string, o: LibraryOpts) => install(o, id)));

  scoped(lib.command("outdated").description("Installed connectors with a new major or a deprecation notice"))
    .action(libraryAction((o: LibraryOpts, cmd: Command) => outdated(o, cmd)));

  scoped(lib.command("update <id>").description("Upgrade an installed connector to its latest major (workspace admin)"))
    .action(libraryAction((id: string, o: LibraryOpts) => update(o, id)));

  registerAuthoringGroup(lib, "connector");
  registerAuthoringGroup(lib, "template");
  // FUNCTIONAL.md §23 documents the authoring commands at the top level (`fmz connector validate …`);
  // both spellings work.
  registerAuthoringGroup(program, "connector");
  registerAuthoringGroup(program, "template");
}
