import { Command } from "commander";
import { createClient } from "../lib/api-client";
import { connectCommunity, type CommunitySource, type SourceWorkflow } from "../lib/community-source";
import { error, info, printTable, success, warn } from "../lib/output";
import { describeError, requireWorkspaceId, unwrap } from "../lib/util";

// `fmz cloud migrate`: copies the workflows of a self-hosted Community Edition install into a Flowamaz Cloud
// workspace (FUNCTIONAL.md §14.4; docs/COMMUNITY-EDITION.md C5). Each workflow arrives with the version it runs in
// production (published again in the cloud) and its current draft. Credentials are never copied: they are listed
// to be added again. Run history and older versions stay readable in the self-hosted install.

/** Where one workflow goes in the cloud workspace. */
export interface Destination {
  existingSlugs(): Promise<Set<string>>;
  create(workflow: { name: string; slug: string; yamlContent: string; description: string | null }): Promise<string>;
  save(id: string, yamlContent: string): Promise<void>;
  publish(id: string): Promise<void>;
  archive(id: string): Promise<void>;
}

export type Outcome = "migrated" | "skipped" | "failed" | "would migrate";

export interface MigrationResult {
  name: string;
  slug: string;
  status: SourceWorkflow["status"];
  outcome: Outcome;
  detail: string;
}

/** Copies every workflow; one failure doesn't stop the rest. */
export async function migrateWorkflows(
  source: CommunitySource,
  destination: Destination,
  dryRun: boolean
): Promise<MigrationResult[]> {
  const workflows = await source.listWorkflows();
  const taken = await destination.existingSlugs();
  const results: MigrationResult[] = [];

  for (const wf of workflows) {
    const base = { name: wf.name, slug: wf.slug, status: wf.status };
    if (taken.has(wf.slug)) {
      results.push({ ...base, outcome: "skipped", detail: "the cloud workspace already has a workflow with this slug" });
      continue;
    }
    try {
      const production = (await source.listVersions(wf.id)).find((v) => v.isProduction) ?? null;
      const productionYaml = production ? await source.versionYaml(wf.id, production.id) : null;
      const draftDiffers = productionYaml !== null && productionYaml !== wf.yamlContent;
      const steps = [
        productionYaml ? "published version" : "draft",
        draftDiffers ? "newer draft" : null,
        wf.status === "Archived" ? "archived" : null,
      ].filter(Boolean).join(" + ");

      if (dryRun) {
        results.push({ ...base, outcome: "would migrate", detail: steps });
        continue;
      }

      const id = await destination.create({
        name: wf.name, slug: wf.slug, yamlContent: productionYaml ?? wf.yamlContent, description: wf.description,
      });
      if (productionYaml) await destination.publish(id);
      if (draftDiffers) await destination.save(id, wf.yamlContent);
      if (wf.status === "Archived" && productionYaml) await destination.archive(id);
      taken.add(wf.slug);
      results.push({ ...base, outcome: "migrated", detail: steps });
    } catch (err) {
      results.push({ ...base, outcome: "failed", detail: describeError(err) });
    }
  }
  return results;
}

async function cloudDestination(workspace?: string): Promise<Destination> {
  const ws = await requireWorkspaceId(workspace);
  const client = await createClient();
  const base = "/api/v1/workspaces/" + encodeURIComponent(ws) + "/workflows";
  return {
    async existingSlugs() {
      const slugs = new Set<string>();
      for (let page = 1; ; page++) {
        const data = unwrap((await client.get(base, { params: { page, pageSize: 100 } })).data) as unknown;
        const rows = Array.isArray(data) ? data : ((data as { data?: unknown[] })?.data ?? []);
        for (const row of rows as { slug?: string }[]) if (row.slug) slugs.add(row.slug);
        if (rows.length < 100) break;
      }
      return slugs;
    },
    async create(wf) {
      const created = unwrap((await client.post(base, {
        name: wf.name, slug: wf.slug, yamlContent: wf.yamlContent, nlDescription: wf.description, createdByMethod: "Canvas",
      })).data) as { id: string };
      return created.id;
    },
    async save(id, yamlContent) {
      await client.put(base + "/" + encodeURIComponent(id), { yamlContent });
    },
    async publish(id) {
      await client.post(base + "/" + encodeURIComponent(id) + "/publish", {});
    },
    async archive(id) {
      await client.post(base + "/" + encodeURIComponent(id) + "/archive", {});
    },
  };
}

/** Reads a password without echoing it; from FMZ_SOURCE_PASSWORD when set (scripts, CI). */
async function readPassword(prompt: string): Promise<string> {
  const fromEnv = process.env.FMZ_SOURCE_PASSWORD;
  if (fromEnv) return fromEnv;
  if (!process.stdin.isTTY) {
    throw new Error("No terminal to ask for the password. Set FMZ_SOURCE_PASSWORD for the Community install's password.");
  }
  process.stdout.write(prompt);
  return new Promise((resolve) => {
    const stdin = process.stdin;
    let value = "";
    stdin.setRawMode(true);
    stdin.resume();
    stdin.setEncoding("utf8");
    const onData = (char: string): void => {
      if (char === "\r" || char === "\n" || char === "\u0004") {
        stdin.setRawMode(false);
        stdin.pause();
        stdin.removeListener("data", onData);
        process.stdout.write("\n");
        resolve(value);
      } else if (char === "\u0003") {
        process.stdout.write("\n");
        process.exit(130);
      } else if (char === "\u007f") {
        value = value.slice(0, -1);
      } else {
        value += char;
      }
    };
    stdin.on("data", onData);
  });
}

export function registerCloudCommands(program: Command): void {
  const cloud = program.command("cloud").description("Move to Flowamaz Cloud");
  cloud
    .command("migrate")
    .description(
      "Copy the workflows of a self-hosted Community Edition install into a Flowamaz Cloud workspace. " +
        "Sign in to the cloud first (fmz auth login). Credentials aren't copied: add them again in the cloud."
    )
    .requiredOption("--from <url>", "The Community install's address, e.g. http://localhost:3000")
    .requiredOption("--from-email <email>", "Your email on the Community install (the password is asked for, or FMZ_SOURCE_PASSWORD)")
    .option("--workspace <slug-or-id>", "Cloud workspace to copy into (default: the current one)")
    .option("--dry-run", "Show what would be copied, change nothing", false)
    .action(async (opts: { from: string; fromEmail: string; workspace?: string; dryRun: boolean }) => {
      const password = await readPassword("Password for " + opts.fromEmail + " on " + opts.from + ": ");
      const source = await connectCommunity(opts.from, opts.fromEmail, password);
      info("Signed in to " + source.instanceName + " (" + opts.from + ").");
      const destination = await cloudDestination(opts.workspace);
      const results = await migrateWorkflows(source, destination, opts.dryRun);

      if (results.length === 0) {
        info("There are no workflows to copy on " + opts.from + ".");
      } else {
        printTable(["Workflow", "Slug", "Status there", "Result", "Detail"],
          results.map((r) => [r.name, r.slug, r.status, r.outcome, r.detail]));
      }
      const failed = results.filter((r) => r.outcome === "failed").length;
      const migrated = results.filter((r) => r.outcome === "migrated").length;

      const credentials = await source.listCredentials();
      if (credentials.length > 0) {
        warn("Credentials aren't copied (their values never leave the install). Add these again in the cloud, Settings → Credentials:");
        printTable(["Connector", "Name"], credentials.map((c) => [c.connectorId, c.name]));
      }
      if (opts.dryRun) info("Dry run: nothing was changed. Run it again without --dry-run to copy.");
      else if (failed > 0) error(failed + " workflow(s) couldn't be copied (see Detail). Fix them and run the command again; copied ones are skipped.");
      else success(migrated + " workflow(s) copied. Runs and older versions stay readable on " + opts.from + ".");
      if (failed > 0) process.exitCode = 1;
    });
}
