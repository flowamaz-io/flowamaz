import { describe, expect, it } from "vitest";
import { Command } from "commander";
import { registerLibraryCommands } from "../src/commands/library";

describe("library authoring commands", () => {
  it("are available at the top level (as documented) and under `library`", () => {
    const program = new Command();
    registerLibraryCommands(program);
    const sub = (cmd: Command, name: string) => cmd.commands.find((c) => c.name() === name);

    for (const kind of ["connector", "template"]) {
      for (const parent of [program, sub(program, "library")!]) {
        const group = sub(parent, kind);
        expect(group, `${parent.name() || "fmz"} ${kind}`).toBeDefined();
        expect(group!.commands.map((c) => c.name())).toEqual(expect.arrayContaining(["init", "validate", "publish"]));
      }
    }
  });
});

import { kindOf, manifestProblems } from "../src/commands/library";
import { noOpMessage } from "../src/lib/util";

describe("library manifests and options", () => {
  it("validates connector manifests with the Library sync's rules", () => {
    expect(manifestProblems("connector", { id: "my-connector", version: "1.0.0" })).toEqual([]);
    expect(manifestProblems("connector", { id: "My Connector", version: "1.0" })).toHaveLength(2);
    expect(manifestProblems("connector", { id: "x", version: "1.0.0", tier: "official" })[0]).toMatch(/Only flowamaz-io/);
    expect(manifestProblems("connector", { id: "x", version: "1.0.0", network: { allowed_domains: ["*.acme.test"] } })[0]).toMatch(/wildcard/);
  });

  it("needs a workflow in a template manifest", () => {
    expect(manifestProblems("template", { name: "T", version: "0.1.0", workflow: {} })[0]).toMatch(/workflow is empty/);
    expect(manifestProblems("template", { name: "T", version: "0.1.0", workflow: { apiVersion: "flowamaz/v1" } })).toEqual([]);
  });

  it("reads --templates, --template and --type template as templates", () => {
    expect(kindOf({})).toBe("connectors");
    expect(kindOf({ templates: true })).toBe("templates");
    expect(kindOf({ template: true })).toBe("templates");
    expect(kindOf({ type: "template" })).toBe("templates");
    expect(kindOf({ type: "connector" })).toBe("connectors");
  });

  it("reads --forms and --packs (or --type form|pack)", () => {
    expect(kindOf({ forms: true })).toBe("forms");
    expect(kindOf({ type: "form" })).toBe("forms");
    expect(kindOf({ packs: true })).toBe("packs");
    expect(kindOf({ type: "packs" })).toBe("packs");
    expect(kindOf({ packs: true, templates: true })).toBe("packs");
  });
});

describe("noOpMessage", () => {
  const axiosError = (status: number, data: unknown) =>
    Object.assign(new Error("x"), { isAxiosError: true, response: { status, data } });

  it("returns the server message for a 409 with an expected code", () => {
    const err = axiosError(409, { code: "LIBRARY_CONNECTOR_INSTALLED", message: "Slack is already installed here." });
    expect(noOpMessage(err, ["LIBRARY_CONNECTOR_INSTALLED"])).toBe("Slack is already installed here.");
  });

  it("ignores other codes and statuses, so real failures still fail", () => {
    expect(noOpMessage(axiosError(409, { code: "LIBRARY_ITEM_REMOVED", message: "gone" }), ["LIBRARY_CONNECTOR_INSTALLED"])).toBeNull();
    expect(noOpMessage(axiosError(400, { code: "LIBRARY_CONNECTOR_INSTALLED" }), ["LIBRARY_CONNECTOR_INSTALLED"])).toBeNull();
    expect(noOpMessage(new Error("boom"), ["LIBRARY_CONNECTOR_INSTALLED"])).toBeNull();
  });
});
