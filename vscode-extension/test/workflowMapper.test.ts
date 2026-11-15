import { describe, it, expect } from "vitest";
import { mapWorkflowsToTree } from "../src/core/workflowMapper";

describe("mapWorkflowsToTree", () => {
  it("maps workflows to tree nodes with status and version", () => {
    const nodes = mapWorkflowsToTree([
      { id: "wf-1", name: "Onboarding", status: "published", version: 3 },
    ]);
    expect(nodes).toHaveLength(1);
    expect(nodes[0]).toMatchObject({
      id: "wf-1",
      label: "Onboarding",
      description: "published · v3",
      status: "published",
    });
  });

  it("falls back to id when name is missing and skips entries without id", () => {
    const nodes = mapWorkflowsToTree([{ id: "wf-2" }, { name: "no id" }]);
    expect(nodes).toHaveLength(1);
    expect(nodes[0].label).toBe("wf-2");
    expect(nodes[0].status).toBe("unknown");
  });
});
