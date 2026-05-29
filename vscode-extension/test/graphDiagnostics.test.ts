import { describe, it, expect } from "vitest";
import { findGraphDiagnostics } from "../src/core/graphDiagnostics";

const VALID = `kind: Workflow
workflow:
  id: wf
  version: 1
  name: Valid
nodes:
  - id: start
    type: trigger
    label: Start
  - id: finish
    type: end
    label: Done
edges:
  - id: e1
    from: start
    to: finish
`;

const ORPHANED = `kind: Workflow
workflow:
  id: wf
  name: Orphan
nodes:
  - id: start
    type: trigger
  - id: lonely
    type: action
  - id: finish
    type: end
edges:
  - id: e1
    from: start
    to: finish
`;

describe("findGraphDiagnostics", () => {
  it("returns no diagnostics for a valid workflow", () => {
    expect(findGraphDiagnostics(VALID)).toEqual([]);
  });

  it("flags an orphaned node with its id", () => {
    const diags = findGraphDiagnostics(ORPHANED);
    const orphan = diags.find((d) => d.message.includes("lonely"));
    expect(orphan).toBeDefined();
    expect(orphan?.severity).toBe("warning");
    expect(orphan?.message).toContain("orphaned");
  });

  it("flags an edge that references a missing node", () => {
    const yaml = `nodes:
  - id: start
    type: trigger
  - id: finish
    type: end
edges:
  - id: e1
    from: start
    to: ghost
`;
    const diags = findGraphDiagnostics(yaml);
    expect(diags.some((d) => d.message.includes("ghost"))).toBe(true);
  });

  it("flags a missing trigger and missing end", () => {
    const yaml = `nodes:
  - id: a
    type: action
edges: []
`;
    const diags = findGraphDiagnostics(yaml);
    expect(diags.some((d) => d.message.includes("no trigger"))).toBe(true);
    expect(diags.some((d) => d.message.includes("no end"))).toBe(true);
  });

  it("returns a parse-error diagnostic for malformed YAML", () => {
    const diags = findGraphDiagnostics("nodes:\n  - id: a\n   bad: : :");
    expect(diags.length).toBe(1);
    expect(diags[0].severity).toBe("error");
    expect(diags[0].message).toContain("parse error");
  });

  it("returns nothing for non-workflow YAML", () => {
    expect(findGraphDiagnostics("foo: bar\nbaz: 1")).toEqual([]);
  });
});
