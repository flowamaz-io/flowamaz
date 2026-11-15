import { describe, it, expect, vi, beforeEach } from "vitest";
import fs from "fs";
import { renderValidation, runValidate } from "../src/commands/workflow";
import type { AxiosInstance } from "axios";

vi.mock("axios");

describe("renderValidation", () => {
  it("includes error messages and line numbers, exit code 1", () => {
    const result = renderValidation({
      errors: [{ message: "X", line: 3 }],
      warnings: [],
    });
    expect(result.output).toContain("X");
    expect(result.output).toContain("3");
    expect(result.exitCode).toBe(1);
  });

  it("includes warnings but keeps exit code 0", () => {
    const result = renderValidation({
      errors: [],
      warnings: [{ message: "deprecated step", line: 7 }],
    });
    expect(result.output).toContain("deprecated step");
    expect(result.output).toContain("7");
    expect(result.exitCode).toBe(0);
  });

  it("reports valid when there are no issues", () => {
    const result = renderValidation({ errors: [], warnings: [] });
    expect(result.output.toLowerCase()).toContain("valid");
    expect(result.exitCode).toBe(0);
  });
});

describe("runValidate", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("calls the API, renders, and returns exit code 1 on errors", async () => {
    vi.spyOn(fs, "readFileSync").mockReturnValue("name: demo");
    const post = vi.fn().mockResolvedValue({
      data: { errors: [{ message: "Bad node", line: 5 }], warnings: [] },
    });
    const client = { post } as unknown as AxiosInstance;

    const lines: string[] = [];
    const code = await runValidate(client, "ops", "wf.yaml", (l) =>
      lines.push(l)
    );

    expect(post).toHaveBeenCalledWith(
      "/api/v1/workspaces/ops/workflows/validate",
      { yamlContent: "name: demo" }
    );
    expect(lines.join("\n")).toContain("Bad node");
    expect(lines.join("\n")).toContain("5");
    expect(code).toBe(1);
  });

  it("returns exit code 0 when the API reports no errors", async () => {
    vi.spyOn(fs, "readFileSync").mockReturnValue("name: demo");
    const post = vi.fn().mockResolvedValue({
      data: { errors: [], warnings: [] },
    });
    const client = { post } as unknown as AxiosInstance;

    const code = await runValidate(client, "ops", "wf.yaml", () => {});
    expect(code).toBe(0);
  });
});
