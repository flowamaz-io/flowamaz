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
