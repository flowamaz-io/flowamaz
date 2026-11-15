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
