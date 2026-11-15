import chalk from "chalk";
import Table from "cli-table3";
import yaml from "js-yaml";

export interface OutputOpts {
  json?: boolean;
  yaml?: boolean;
  quiet?: boolean;
}

let quietMode = false;

export function setQuiet(value: boolean): void {
  quietMode = value;
}

/**
 * Pure table renderer — returns the rendered string so it is testable
 * without touching stdout.
 */
export function renderTable(headers: string[], rows: string[][]): string {
  const table = new Table({ head: headers });
  for (const row of rows) {
    table.push(row);
  }
  return table.toString();
}

export function printTable(headers: string[], rows: string[][]): void {
  process.stdout.write(renderTable(headers, rows) + "\n");
}

/**
 * Format arbitrary data honoring --json / --yaml. Default returns a
 * human-readable string (pretty JSON for objects, the value for scalars).
 */
export function format(data: unknown, opts: OutputOpts = {}): string {
  if (opts.json) {
    return JSON.stringify(data, null, 2);
  }
  if (opts.yaml) {
    return yaml.dump(data);
  }
  if (data === null || data === undefined) {
    return "";
  }
  if (typeof data === "string") {
    return data;
  }
  if (typeof data === "number" || typeof data === "boolean") {
    return String(data);
  }
  return JSON.stringify(data, null, 2);
}

export function printFormatted(data: unknown, opts: OutputOpts = {}): void {
  const out = format(data, opts);
  if (out.length > 0) {
    process.stdout.write(out + "\n");
  }
}

export function success(message: string): void {
  if (quietMode) {
    return;
  }
  process.stdout.write(chalk.green("✓ " + message) + "\n");
}

export function info(message: string): void {
  if (quietMode) {
    return;
  }
  process.stdout.write(chalk.gray(message) + "\n");
}

export function warn(message: string): void {
  process.stderr.write(chalk.yellow("! " + message) + "\n");
}

export function error(message: string): void {
  process.stderr.write(chalk.red("✗ " + message) + "\n");
}
