import { describe, expect, it, vi } from "vitest";
import type { AxiosInstance } from "axios";
import { migrateWorkflows, type Destination } from "../src/commands/cloud";
import { connectCommunity, sourceOrigin, type CommunitySource, type SourceWorkflow } from "../src/lib/community-source";

const wf = (slug: string, status: SourceWorkflow["status"], yamlContent = "draft:" + slug): SourceWorkflow => ({
  id: "id-" + slug, name: slug.toUpperCase(), slug, status, yamlContent, description: null,
});

function source(workflows: SourceWorkflow[], production: Record<string, string> = {}): CommunitySource {
  return {
    instanceName: "Acme",
    listWorkflows: async () => workflows,
    listVersions: async (id) => (production[id] ? [{ id: "v-" + id, isProduction: true, createdAt: "", tagName: null }] : []),
    versionYaml: async (id) => production[id],
    listCredentials: async () => [{ connectorId: "slack", name: "team" }],
  };
}

function destination(existing: string[] = []): Destination & { calls: string[] } {
  const calls: string[] = [];
  return {
    calls,
    existingSlugs: async () => new Set(existing),
    create: vi.fn(async (w) => { calls.push("create " + w.slug + " " + w.yamlContent); return "new-" + w.slug; }),
    save: vi.fn(async (id, yaml) => { calls.push("save " + id + " " + yaml); }),
    publish: vi.fn(async (id) => { calls.push("publish " + id); }),
    archive: vi.fn(async (id) => { calls.push("archive " + id); }),
  };
}

describe("fmz cloud migrate", () => {
  it("publishes the production version, then keeps a newer draft as the draft", async () => {
    const dest = destination();
    const results = await migrateWorkflows(
      source([wf("leave", "Published", "draft-v2")], { "id-leave": "prod-v1" }), dest, false);

    expect(dest.calls).toEqual(["create leave prod-v1", "publish new-leave", "save new-leave draft-v2"]);
    expect(results[0]).toMatchObject({ outcome: "migrated", detail: "published version + newer draft" });
  });

  it("copies a draft as a draft, and an archived workflow archived", async () => {
    const dest = destination();
    await migrateWorkflows(source([wf("idea", "Draft"), wf("old", "Archived", "y")], { "id-old": "y" }), dest, false);
    expect(dest.calls).toEqual(["create idea draft:idea", "create old y", "publish new-old", "archive new-old"]);
  });

  it("skips slugs the cloud already has and carries on after a failure", async () => {
    const dest = destination(["taken"]);
    (dest.create as ReturnType<typeof vi.fn>).mockImplementationOnce(async () => { throw new Error("YAML invalid: fix it"); });
    const results = await migrateWorkflows(source([wf("taken", "Draft"), wf("bad", "Draft"), wf("good", "Draft")]), dest, false);
    expect(results.map((r) => r.outcome)).toEqual(["skipped", "failed", "migrated"]);
    expect(results[1].detail).toContain("YAML invalid");
  });

  it("changes nothing on a dry run", async () => {
    const dest = destination();
    const results = await migrateWorkflows(source([wf("leave", "Published")], { "id-leave": "draft:leave" }), dest, true);
    expect(dest.calls).toEqual([]);
    expect(results[0]).toMatchObject({ outcome: "would migrate", detail: "published version" });
  });
});

describe("connecting to the Community install", () => {
  const http = (status: unknown, login?: unknown) => ({
    get: vi.fn(async () => status),
    post: vi.fn(async () => login),
  }) as unknown as AxiosInstance;

  it("normalises the address", () => {
    expect(sourceOrigin("flowamaz.local:3000/workflows")).toBe("http://flowamaz.local:3000");
    expect(sourceOrigin("https://f.example.com/")).toBe("https://f.example.com");
  });

  it("says when the address isn't a Community install, or isn't set up", async () => {
    await expect(connectCommunity("http://x", "a@b.c", "p", http({ status: 404, data: {} }))).rejects.toThrow(/doesn't look like/);
    await expect(connectCommunity("http://x", "a@b.c", "p", http({ status: 200, data: { setupNeeded: true } }))).rejects.toThrow(/isn't set up/);
  });

  it("passes on the install's sign-in message", async () => {
    const h = http({ status: 200, data: { setupNeeded: false } }, { status: 401, data: { message: "That email and password don't match." } });
    await expect(connectCommunity("http://x", "a@b.c", "p", h)).rejects.toThrow(/don't match/);
  });
});
