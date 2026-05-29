import { describe, it, expect } from "vitest";
import { isFlowamazWorkflow } from "../src/core/isFlowamazDoc";

describe("isFlowamazWorkflow", () => {
  it("matches by file name", () => {
    expect(isFlowamazWorkflow("foo: bar", "/x/order-flowamaz-workflow.yaml")).toBe(
      true
    );
  });

  it("matches by 'kind: Workflow'", () => {
    expect(isFlowamazWorkflow("kind: Workflow\n", "/x/random.yaml")).toBe(true);
  });

  it("matches when both workflow: and nodes: are present", () => {
    expect(
      isFlowamazWorkflow("workflow:\n  id: x\nnodes: []\n", "/x/random.yaml")
    ).toBe(true);
  });

  it("does not match unrelated YAML", () => {
    expect(isFlowamazWorkflow("name: ci\non: push\n", "/x/ci.yaml")).toBe(false);
  });
});
