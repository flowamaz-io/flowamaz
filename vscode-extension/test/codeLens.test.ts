import { describe, it, expect } from "vitest";
import { computeCodeLenses } from "../src/core/codeLens";

describe("computeCodeLenses", () => {
  it("returns 3 lenses for a 'kind: Workflow' document", () => {
    const text = "kind: Workflow\nworkflow:\n  id: x\n";
    const lenses = computeCodeLenses(text);
    expect(lenses).toHaveLength(3);
    expect(lenses.map((l) => l.title)).toEqual([
      "▶ Trigger",
      "📊 Open in App",
      "✓ Validate",
    ]);
    expect(lenses[0].line).toBe(0);
  });

  it("returns 3 lenses anchored to a top-level workflow: key", () => {
    const text = "# comment\nworkflow:\n  id: x\nnodes: []\n";
    const lenses = computeCodeLenses(text);
    expect(lenses).toHaveLength(3);
    expect(lenses[0].line).toBe(1);
  });

  it("returns 0 lenses for plain YAML", () => {
    expect(computeCodeLenses("foo: bar\nbaz: 1\n")).toHaveLength(0);
  });
});
