import { describe, it, expect } from "vitest";
import { renderTable, format } from "../src/lib/output";

describe("renderTable", () => {
  it("renders the provided column headers", () => {
    const out = renderTable(
      ["ID", "Name", "Slug"],
      [["1", "Ops", "ops"]]
    );
    expect(out).toContain("ID");
    expect(out).toContain("Name");
    expect(out).toContain("Slug");
  });

  it("renders row values", () => {
    const out = renderTable(["Name"], [["Production"]]);
    expect(out).toContain("Production");
  });
});

describe("format", () => {
  it("produces pretty JSON with --json", () => {
    const out = format({ a: 1 }, { json: true });
    expect(out).toBe(JSON.stringify({ a: 1 }, null, 2));
  });

  it("produces YAML with --yaml", () => {
    const out = format({ a: 1 }, { yaml: true });
    expect(out).toContain("a: 1");
  });

  it("returns scalars as strings by default", () => {
    expect(format("hello")).toBe("hello");
    expect(format(42)).toBe("42");
  });
});

import { vi } from "vitest";
import { notice, warn } from "../src/lib/output";

describe("notice", () => {
  it("writes to stdout (so a heading stays above its table), while warn writes to stderr", () => {
    const out = vi.spyOn(process.stdout, "write").mockImplementation(() => true);
    const err = vi.spyOn(process.stderr, "write").mockImplementation(() => true);
    notice("Add these again:");
    warn("careful");
    expect(String(out.mock.calls[0][0])).toContain("Add these again:");
    expect(String(err.mock.calls[0][0])).toContain("careful");
    out.mockRestore();
    err.mockRestore();
  });
});
