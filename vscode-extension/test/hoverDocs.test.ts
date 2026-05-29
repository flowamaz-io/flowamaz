import { describe, it, expect } from "vitest";
import { getHoverDoc } from "../src/core/hoverDocs";

describe("getHoverDoc", () => {
  it("returns documentation containing node-type info for 'type'", () => {
    const doc = getHoverDoc("type");
    expect(doc).toBeDefined();
    expect(doc).toContain("node type");
    expect(doc).toContain("action");
    expect(doc).toContain("trigger");
  });

  it("documents node-type values", () => {
    const doc = getHoverDoc("human-gate");
    expect(doc).toBeDefined();
    expect(doc).toContain("human-gate node");
  });

  it("is case-insensitive", () => {
    expect(getHoverDoc("RETRY")).toBeDefined();
  });

  it("returns undefined for unknown words", () => {
    expect(getHoverDoc("not-a-field")).toBeUndefined();
  });
});
