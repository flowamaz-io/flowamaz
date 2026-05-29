import { describe, it, expect } from "vitest";
import { getSnippets, buildCodeSnippetsFile } from "../src/core/snippets";

describe("getSnippets", () => {
  it("includes a 'fmz-action' snippet", () => {
    const snippets = getSnippets();
    const action = snippets.find((s) => s.label === "fmz-action");
    expect(action).toBeDefined();
    expect(action?.body.join("\n")).toContain("type: action");
  });

  it("returns all 8 documented snippets", () => {
    const labels = getSnippets().map((s) => s.label).sort();
    expect(labels).toEqual(
      [
        "fmz-action",
        "fmz-ai",
        "fmz-foreach",
        "fmz-gate",
        "fmz-parallel",
        "fmz-router",
        "fmz-trycatch",
        "fmz-workflow",
      ].sort()
    );
  });

  it("builds a code-snippets file object keyed by label", () => {
    const file = buildCodeSnippetsFile();
    expect(file["fmz-action"].prefix).toBe("fmz-action");
    expect(Array.isArray(file["fmz-action"].body)).toBe(true);
  });
});
